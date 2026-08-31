using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Taskdeck.Infrastructure.Services;

namespace Taskdeck.Cli.Commands;

/// <summary>
/// Application-consistent encrypted backup and fail-closed restore commands.
/// Both paths execute before normal host construction so they cannot migrate,
/// bootstrap keys, or start application providers.
/// </summary>
internal static class DatabaseRecoveryCommand
{
    private const string BackupUsage =
        "taskdeck-backup --database <path> --output <directory> [--key-file <path>]";
    private const string RestoreUsage =
        "taskdeck-restore --archive <path> --database <path> [--key-file <path>] " +
        "[--connector-key-file <path>]";
    private const int KeyFileSizeLimit = 4096;

    public static bool IsRequest(IReadOnlyList<string>? args) =>
        args is { Count: > 0 } &&
        (string.Equals(args[0], "--backup", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(args[0], "--restore", StringComparison.OrdinalIgnoreCase));

    public static async Task<int> ExecuteAsync(IReadOnlyList<string> args)
    {
        if (!IsRequest(args))
        {
            return ConsoleOutput.PrintUsageError("A recovery command is required.", BackupUsage);
        }

        return string.Equals(args[0], "--backup", StringComparison.OrdinalIgnoreCase)
            ? await ExecuteBackupAsync(args)
            : await ExecuteRestoreAsync(args);
    }

    private static async Task<int> ExecuteBackupAsync(IReadOnlyList<string> args)
    {
        if (!TryParseOptions(
                args,
                new[] { "--database", "--output", "--key-file" },
                new[] { "--database", "--output" },
                out var options,
                out var usageError))
        {
            return ConsoleOutput.PrintUsageError(usageError, BackupUsage);
        }

        if (!TryResolveExistingFile(options["--database"], out var databasePath) ||
            !TryResolveExistingDirectory(options["--output"], out var outputDirectory))
        {
            return ConsoleOutput.PrintFailure(
                "BACKUP_INPUT_UNAVAILABLE",
                "Backup database or output directory is unavailable.");
        }

        var keyResult = await ResolveKeyAsync(
            options.GetValueOrDefault("--key-file"),
            "TASKDECK_BACKUP_KEY_FILE",
            new[] { "TASKDECK_BACKUP_KEY" },
            "BACKUP");
        if (!keyResult.IsSuccess)
        {
            return ConsoleOutput.PrintFailure(keyResult.ErrorCode!, keyResult.ErrorMessage!);
        }

        using var key = keyResult.Key!;
        try
        {
            var result = await CreateBackupAsync(databasePath!, outputDirectory!, key.Bytes);
            Console.WriteLine($"archive={result.ArchivePath}");
            Console.WriteLine($"schema={result.SchemaVersion}");
            Console.WriteLine("integrity=ok");
            return ExitCodes.Success;
        }
        catch (Exception)
        {
            return ConsoleOutput.PrintFailure(
                "BACKUP_FAILED",
                "Backup could not be created.");
        }
    }

    private static async Task<int> ExecuteRestoreAsync(IReadOnlyList<string> args)
    {
        if (!TryParseOptions(
                args,
                new[] { "--archive", "--database", "--key-file", "--connector-key-file" },
                new[] { "--archive", "--database" },
                out var options,
                out var usageError))
        {
            return ConsoleOutput.PrintUsageError(usageError, RestoreUsage);
        }

        if (!TryResolveExistingFile(options["--archive"], out var archivePath) ||
            !TryResolveTargetPath(options["--database"], out var databasePath))
        {
            return ConsoleOutput.PrintFailure(
                "RESTORE_INPUT_UNAVAILABLE",
                "Restore archive or target directory is unavailable.");
        }

        if (string.Equals(archivePath, databasePath, PathComparison))
        {
            return ConsoleOutput.PrintUsageError(
                "Restore archive and database target must be different paths.",
                RestoreUsage);
        }

        var backupKeyResult = await ResolveKeyAsync(
            options.GetValueOrDefault("--key-file"),
            "TASKDECK_BACKUP_KEY_FILE",
            new[] { "TASKDECK_BACKUP_KEY" },
            "BACKUP");
        if (!backupKeyResult.IsSuccess)
        {
            return ConsoleOutput.PrintFailure(
                backupKeyResult.ErrorCode!,
                backupKeyResult.ErrorMessage!);
        }

        using var backupKey = backupKeyResult.Key!;
        var connectorKeyResult = await ResolveKeyAsync(
            options.GetValueOrDefault("--connector-key-file"),
            "TASKDECK_CONNECTOR_KEY_FILE",
            new[] { "TASKDECK_CONNECTORS__ENCRYPTIONKEY", "Connectors__EncryptionKey" },
            "CONNECTOR");
        if (!connectorKeyResult.IsSuccess)
        {
            return ConsoleOutput.PrintFailure(
                connectorKeyResult.ErrorCode!,
                connectorKeyResult.ErrorMessage!);
        }

        using var connectorKey = connectorKeyResult.Key!;
        try
        {
            var result = await RestoreAsync(
                archivePath!,
                databasePath!,
                backupKey.Bytes,
                connectorKey.Base64Value);
            Console.WriteLine($"restored={result.DatabasePath}");
            Console.WriteLine($"schema={result.SchemaVersion}");
            Console.WriteLine("integrity=ok");
            Console.WriteLine($"connectors ok={result.ConnectorCounts.Ok} failed={result.ConnectorCounts.Failed}");
            if (result.SafetyArchivePath is not null)
            {
                Console.WriteLine($"safetyArchive={result.SafetyArchivePath}");
            }

            return ExitCodes.Success;
        }
        catch (ConnectorRestoreVerificationException)
        {
            return ConsoleOutput.PrintFailure(
                "RESTORE_CONNECTOR_VERIFICATION_FAILED",
                "Restored connector credentials could not be verified.");
        }
        catch (Exception)
        {
            return ConsoleOutput.PrintFailure(
                "RESTORE_FAILED",
                "Restore could not be completed.");
        }
    }

    internal static async Task<DatabaseBackupResult> CreateBackupAsync(
        string databasePath,
        string outputDirectory,
        ReadOnlyMemory<byte> key,
        string filePrefix = "taskdeck-backup",
        CancellationToken cancellationToken = default)
    {
        // Keep the plaintext SQLite snapshot out of the mounted archive output.
        // Container invocations are one-shot, so the scratch file lives only in
        // the container's ephemeral filesystem and is removed before promotion.
        var snapshotPath = Path.Combine(
            Path.GetTempPath(),
            $".taskdeck-{Guid.NewGuid():N}.snapshot.db");
        string? temporaryArchivePath = null;
        string? finalArchivePath = null;
        var finalArchivePromoted = false;

        try
        {
            await CreateSqliteSnapshotAsync(databasePath, snapshotPath, cancellationToken);
            var validation = await ValidateDatabaseAsync(snapshotPath, cancellationToken);
            var createdAt = DateTimeOffset.UtcNow;
            finalArchivePath = ReserveArchivePath(
                outputDirectory,
                filePrefix,
                validation.SchemaVersion,
                createdAt);
            temporaryArchivePath = finalArchivePath + $".tmp-{Guid.NewGuid():N}";

            await RecoveryArchive.EncryptAsync(
                snapshotPath,
                temporaryArchivePath,
                key,
                validation.SchemaVersion,
                createdAt,
                cancellationToken);

            DeleteDatabaseArtifacts(snapshotPath);
            File.Move(temporaryArchivePath, finalArchivePath);
            finalArchivePromoted = true;
            RestrictUnixFileMode(finalArchivePath);
            return new DatabaseBackupResult(finalArchivePath, validation.SchemaVersion);
        }
        catch
        {
            TryDeleteFile(temporaryArchivePath);
            if (finalArchivePromoted)
            {
                TryDeleteFile(finalArchivePath);
            }
            TryDeleteDatabaseArtifacts(snapshotPath);
            throw;
        }
    }

    internal static async Task<DatabaseRestoreResult> RestoreAsync(
        string archivePath,
        string databasePath,
        ReadOnlyMemory<byte> backupKey,
        string connectorKey,
        CancellationToken cancellationToken = default,
        Func<string, CancellationToken, Task>? postPromotionProbe = null)
    {
        var targetDirectory = Path.GetDirectoryName(databasePath)
            ?? throw new InvalidOperationException("Restore target directory is unavailable.");
        var stagingPath = Path.Combine(
            targetDirectory,
            $"{Path.GetFileName(databasePath)}.restore-{Guid.NewGuid():N}.db");
        string? safetyArchivePath = null;
        string? rollbackPath = null;
        var targetRemoved = false;
        var databasePromoted = false;

        try
        {
            var metadata = await RecoveryArchive.DecryptAsync(
                archivePath,
                stagingPath,
                backupKey,
                cancellationToken);
            RestrictUnixFileMode(stagingPath);
            var stagingValidation = await ValidateDatabaseAsync(stagingPath, cancellationToken);
            if (!string.Equals(
                    metadata.SchemaVersion,
                    stagingValidation.SchemaVersion,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("Recovery archive schema metadata does not match its database.");
            }

            var stagingCounts = await VerifyConnectorsAsync(
                stagingPath,
                connectorKey,
                cancellationToken);
            if (stagingCounts.Failed != 0)
            {
                throw new ConnectorRestoreVerificationException();
            }

            if (File.Exists(databasePath))
            {
                RejectReparsePoint(databasePath);
                var safety = await CreateBackupAsync(
                    databasePath,
                    targetDirectory,
                    backupKey,
                    "taskdeck-pre-restore",
                    cancellationToken);
                safetyArchivePath = safety.ArchivePath;

                // Preserve a standalone local rollback copy until the promoted
                // database passes the same integrity and connector checks. The
                // encrypted safety archive remains the durable operator copy.
                rollbackPath = Path.Combine(
                    targetDirectory,
                    $".{Path.GetFileName(databasePath)}.rollback-{Guid.NewGuid():N}.db");
                await CreateSqliteSnapshotAsync(databasePath, rollbackPath, cancellationToken);
            }

            DeleteDatabaseArtifacts(databasePath);
            targetRemoved = true;
            File.Move(stagingPath, databasePath);
            databasePromoted = true;
            RestrictUnixFileMode(databasePath);
            if (postPromotionProbe is not null)
            {
                await postPromotionProbe(databasePath, cancellationToken);
            }

            var promotedValidation = await ValidateDatabaseAsync(databasePath, cancellationToken);
            if (!string.Equals(
                    metadata.SchemaVersion,
                    promotedValidation.SchemaVersion,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("Promoted database schema does not match its archive.");
            }

            var promotedCounts = await VerifyConnectorsAsync(
                databasePath,
                connectorKey,
                cancellationToken);
            if (promotedCounts.Failed != 0)
            {
                throw new ConnectorRestoreVerificationException();
            }

            if (rollbackPath is not null)
            {
                DeleteDatabaseArtifacts(rollbackPath);
                rollbackPath = null;
            }

            return new DatabaseRestoreResult(
                databasePath,
                promotedValidation.SchemaVersion,
                promotedCounts,
                safetyArchivePath);
        }
        catch
        {
            TryDeleteDatabaseArtifacts(stagingPath);
            if (databasePromoted)
            {
                TryDeleteDatabaseArtifacts(databasePath);
            }

            if (targetRemoved && rollbackPath is not null && File.Exists(rollbackPath))
            {
                try
                {
                    File.Move(rollbackPath, databasePath);
                    rollbackPath = null;
                }
                catch (Exception exception) when (IsFileSystemException(exception))
                {
                    // The encrypted safety archive and restricted rollback
                    // copy are retained when automatic rollback cannot finish.
                }
            }
            throw;
        }
    }

    private static async Task CreateSqliteSnapshotAsync(
        string databasePath,
        string snapshotPath,
        CancellationToken cancellationToken)
    {
        RejectReparsePoint(databasePath);
        var sourceConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString();
        var destinationConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = snapshotPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString();

        await using (var source = new SqliteConnection(sourceConnectionString))
        await using (var destination = new SqliteConnection(destinationConnectionString))
        {
            await source.OpenAsync(cancellationToken);
            await destination.OpenAsync(cancellationToken);
            RestrictUnixFileMode(snapshotPath);
            source.BackupDatabase(destination);
            await using var journalMode = destination.CreateCommand();
            journalMode.CommandText = "PRAGMA journal_mode=DELETE;";
            var result = await journalMode.ExecuteScalarAsync(cancellationToken);
            if (!string.Equals(Convert.ToString(result, CultureInfo.InvariantCulture), "delete", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Recovery snapshot journal mode is not standalone.");
            }
        }
        EnsureNoJournalSidecars(snapshotPath);
    }

    private static async Task<DatabaseValidationResult> ValidateDatabaseAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        EnsureSqliteHeader(databasePath);
        EnsureNoJournalSidecars(databasePath);
        await using var connection = new SqliteConnection(
            ConnectorVerificationCommand.CreateReadOnlyConnectionString(databasePath));
        await connection.OpenAsync(cancellationToken);

        await using (var integrity = connection.CreateCommand())
        {
            integrity.CommandText = "PRAGMA integrity_check;";
            var result = Convert.ToString(
                await integrity.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture);
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Recovery database integrity check failed.");
            }
        }

        string? schemaVersion;
        await using (var schema = connection.CreateCommand())
        {
            schema.CommandText =
                "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" " +
                "ORDER BY \"MigrationId\" DESC LIMIT 1;";
            schemaVersion = Convert.ToString(
                await schema.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture);
        }

        if (string.IsNullOrWhiteSpace(schemaVersion))
        {
            throw new InvalidDataException("Recovery database schema version is unavailable.");
        }

        EnsureNoJournalSidecars(databasePath);
        return new DatabaseValidationResult(schemaVersion);
    }

    private static async Task<ConnectorVerificationCounts> VerifyConnectorsAsync(
        string databasePath,
        string connectorKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var encryption = new AesCredentialEncryptionService(connectorKey);
        return await ConnectorVerificationCommand.VerifyDatabaseAsync(databasePath, encryption);
    }

    private static bool TryParseOptions(
        IReadOnlyList<string> args,
        IReadOnlyCollection<string> allowed,
        IReadOnlyCollection<string> required,
        out Dictionary<string, string> options,
        out string usageError)
    {
        options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        usageError = string.Empty;
        for (var index = 1; index < args.Count; index += 2)
        {
            var option = args[index];
            if (!allowed.Contains(option, StringComparer.OrdinalIgnoreCase))
            {
                usageError = $"Unknown recovery option: {option}.";
                return false;
            }

            if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                usageError = $"Option {option} requires a value.";
                return false;
            }

            if (!options.TryAdd(option, args[index + 1]))
            {
                usageError = $"Option {option} may be supplied only once.";
                return false;
            }
        }

        foreach (var option in required)
        {
            if (!options.TryGetValue(option, out var value) || string.IsNullOrWhiteSpace(value))
            {
                usageError = $"Recovery command requires {option} <path>.";
                return false;
            }
        }

        return true;
    }

    private static async Task<KeyResolutionResult> ResolveKeyAsync(
        string? explicitKeyFile,
        string keyFileEnvironmentVariable,
        IReadOnlyList<string> keyValueEnvironmentVariables,
        string keyKind)
    {
        var keyFile = !string.IsNullOrWhiteSpace(explicitKeyFile)
            ? explicitKeyFile
            : Environment.GetEnvironmentVariable(keyFileEnvironmentVariable);
        string? value;
        if (!string.IsNullOrWhiteSpace(keyFile))
        {
            if (!TryResolveExistingFile(keyFile, out var keyPath))
            {
                return KeyResolutionResult.Failure(
                    $"{keyKind}_KEY_UNAVAILABLE",
                    KeyMessage(keyKind, "encryption key file is unavailable."));
            }

            try
            {
                if (new FileInfo(keyPath!).Length > KeyFileSizeLimit)
                {
                    throw new InvalidDataException("Key file is too large.");
                }

                value = (await File.ReadAllTextAsync(keyPath!)).Trim();
            }
            catch (Exception)
            {
                return KeyResolutionResult.Failure(
                    $"{keyKind}_KEY_UNAVAILABLE",
                    KeyMessage(keyKind, "encryption key file is unavailable."));
            }
        }
        else
        {
            value = keyValueEnvironmentVariables
                .Select(Environment.GetEnvironmentVariable)
                .FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate))
                ?.Trim();
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return KeyResolutionResult.Failure(
                $"{keyKind}_KEY_MISSING",
                KeyMessage(keyKind, "encryption key was not supplied."));
        }

        try
        {
            var bytes = Convert.FromBase64String(value);
            if (bytes.Length != 32)
            {
                CryptographicOperations.ZeroMemory(bytes);
                throw new FormatException("Key length is invalid.");
            }

            return KeyResolutionResult.Success(
                new RecoveryKey(bytes, Convert.ToBase64String(bytes)));
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return KeyResolutionResult.Failure(
                $"{keyKind}_KEY_INVALID",
                KeyMessage(keyKind, "encryption key is invalid."));
        }
    }

    private static string KeyMessage(string keyKind, string suffix) =>
        string.Equals(keyKind, "BACKUP", StringComparison.Ordinal)
            ? $"Backup {suffix}"
            : $"Connector {suffix}";

    private static bool TryResolveExistingFile(string path, out string? fullPath)
    {
        if (!TryGetFullPath(path, out fullPath) || !File.Exists(fullPath))
        {
            return false;
        }

        try
        {
            return (File.GetAttributes(fullPath) & FileAttributes.Directory) == 0;
        }
        catch (Exception exception) when (IsFileSystemException(exception))
        {
            return false;
        }
    }

    private static bool TryResolveExistingDirectory(string path, out string? fullPath) =>
        TryGetFullPath(path, out fullPath) && Directory.Exists(fullPath);

    private static bool TryResolveTargetPath(string path, out string? fullPath)
    {
        if (!TryGetFullPath(path, out fullPath))
        {
            return false;
        }

        var parent = Path.GetDirectoryName(fullPath);
        return !string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent);
    }

    private static bool TryGetFullPath(string path, out string? fullPath)
    {
        try
        {
            fullPath = Path.GetFullPath(path);
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            fullPath = null;
            return false;
        }
    }

    private static string ReserveArchivePath(
        string directory,
        string prefix,
        string schemaVersion,
        DateTimeOffset createdAt)
    {
        var timestamp = createdAt.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
        var safeSchema = SanitizeFileNameComponent(schemaVersion);
        for (var sequence = 1; sequence < int.MaxValue; sequence++)
        {
            var candidate = Path.Combine(
                directory,
                $"{prefix}-{timestamp}-schema-{safeSchema}-{sequence:D6}.tdbk");
            if (!File.Exists(candidate) && !File.Exists(candidate + ".tmp"))
            {
                return candidate;
            }
        }

        throw new IOException("A recovery archive filename could not be reserved.");
    }

    private static string SanitizeFileNameComponent(string value)
    {
        var builder = new StringBuilder(Math.Min(value.Length, 96));
        foreach (var character in value.Take(96))
        {
            builder.Append(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'
                ? character
                : '_');
        }

        return builder.Length == 0 ? "unknown" : builder.ToString();
    }

    private static void EnsureSqliteHeader(string databasePath)
    {
        Span<byte> header = stackalloc byte[16];
        using var stream = new FileStream(databasePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Read(header) != header.Length || !header[..15].SequenceEqual("SQLite format 3"u8))
        {
            throw new InvalidDataException("Recovery database header is invalid.");
        }
    }

    private static void EnsureNoJournalSidecars(string databasePath)
    {
        if (JournalSidecars(databasePath).Any(File.Exists))
        {
            throw new InvalidOperationException("Recovery database has journal sidecars.");
        }
    }

    private static void DeleteJournalSidecars(string databasePath)
    {
        foreach (var sidecar in JournalSidecars(databasePath))
        {
            File.Delete(sidecar);
        }

        EnsureNoJournalSidecars(databasePath);
    }

    private static IEnumerable<string> JournalSidecars(string databasePath)
    {
        yield return databasePath + "-wal";
        yield return databasePath + "-shm";
        yield return databasePath + "-journal";
    }

    private static void DeleteDatabaseArtifacts(string databasePath)
    {
        DeleteJournalSidecars(databasePath);
        File.Delete(databasePath);
        if (File.Exists(databasePath))
        {
            throw new IOException("Recovery staging database could not be removed.");
        }
    }

    private static void TryDeleteDatabaseArtifacts(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            return;
        }

        TryDeleteFile(databasePath);
        foreach (var sidecar in JournalSidecars(databasePath))
        {
            TryDeleteFile(sidecar);
        }
    }

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (IsFileSystemException(exception))
        {
            // The command still fails. Cleanup is best-effort after an earlier failure.
        }
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("Recovery paths must not be symbolic links.");
        }
    }

    private static void RestrictUnixFileMode(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static bool IsFileSystemException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or NotSupportedException;

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private sealed class RecoveryKey : IDisposable
    {
        public RecoveryKey(byte[] bytes, string base64Value)
        {
            Bytes = bytes;
            Base64Value = base64Value;
        }

        public byte[] Bytes { get; }
        public string Base64Value { get; }

        public void Dispose() => CryptographicOperations.ZeroMemory(Bytes);
    }

    private sealed record KeyResolutionResult(
        bool IsSuccess,
        RecoveryKey? Key,
        string? ErrorCode,
        string? ErrorMessage)
    {
        public static KeyResolutionResult Success(RecoveryKey key) =>
            new(true, key, null, null);

        public static KeyResolutionResult Failure(string code, string message) =>
            new(false, null, code, message);
    }

    private sealed class ConnectorRestoreVerificationException : Exception;
}

internal sealed record DatabaseBackupResult(string ArchivePath, string SchemaVersion);

internal sealed record DatabaseRestoreResult(
    string DatabasePath,
    string SchemaVersion,
    ConnectorVerificationCounts ConnectorCounts,
    string? SafetyArchivePath);

internal sealed record DatabaseValidationResult(string SchemaVersion);
