using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Taskdeck.Application.Bootstrap;

namespace Taskdeck.Cli;

/// <summary>
/// First-run bootstrap for the standalone CLI.
///
/// The CLI ships no <c>appsettings.json</c> and never runs the API's
/// <c>FirstRunBootstrapper</c>, yet <c>AddInfrastructure</c> fail-fasts when
/// <c>Connectors:EncryptionKey</c> is missing. On a clean machine that made
/// <c>taskdeck boards list</c> crash at startup unless the operator exported a
/// key by hand (<c>Connectors__EncryptionKey</c>, or
/// <c>TASKDECK_CONNECTORS__ENCRYPTIONKEY</c> now that the CLI host registers the
/// <c>TASKDECK_</c> prefix).
///
/// This helper provisions a cryptographically-random 256-bit connector key on
/// first run, persists it to a CLI-local <c>appsettings.local.json</c> next to
/// the resolved SQLite data directory (a user-writable location alongside the
/// data it protects), and loads it into configuration so
/// <c>AddInfrastructure</c> succeeds.
///
/// The desktop API and CLI both resolve the default local-config file beside the
/// resolved SQLite database and coordinate first-run writes with the same per-path
/// cross-process lock. A deployment that uses a custom config path, or several
/// hosts with separate files against one database, must still set one explicit
/// <c>Connectors__EncryptionKey</c> (environment variable or appsettings) on every
/// host. The bootstrap emits a stderr warning when it auto-generates a key for a
/// non-default database location (<c>TASKDECK_CONNECTION_STRING</c> set).
///
/// Must run BEFORE the DI container is built and must never write to stdout
/// (the CLI keeps stdout clean JSON for callers); diagnostics go to stderr only.
/// </summary>
internal static class CliFirstRunBootstrapper
{
    private const string ConnectorSection = "Connectors";
    private const string EncryptionKeyName = "EncryptionKey";
    private const string ConnectorKeyPath = $"{ConnectorSection}:{EncryptionKeyName}";
    private const string LocalConfigFileName = "appsettings.local.json";

    /// <summary>
    /// Ensures a connector encryption key is available in configuration,
    /// generating and persisting one on first run when none is configured.
    /// Returns early (no file I/O) when a key is already present in the live
    /// <see cref="IConfiguration"/> -- i.e. an environment variable
    /// (<c>Connectors__EncryptionKey</c>, or <c>TASKDECK_CONNECTORS__ENCRYPTIONKEY</c>
    /// once the CLI host registers the <c>TASKDECK_</c> prefix) or an appsettings
    /// entry. The previously generated <c>appsettings.local.json</c> is NOT
    /// registered as a configuration source, so it does not trigger this early
    /// return; instead <see cref="EnsureKeyOnDisk"/> re-reads that file on every
    /// run and reuses the persisted key (idempotent).
    /// </summary>
    public static void EnsureConnectorEncryptionKey(IConfiguration configuration)
    {
        // Respect an explicitly configured key (environment variable or
        // appsettings). Environment variables must always win over the generated
        // file so 12-factor / container deployments are never silently overridden.
        var configured = configuration[ConnectorKeyPath];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return;
        }

        var localConfigPath = ResolveLocalConfigPath(configuration);
        var databasePath = ResolveDatabaseFilePath(configuration);
        var key = EnsureKeyOnDisk(localConfigPath, databasePath);

        // Load the key into the live configuration so AddInfrastructure can read
        // it. The CLI host does not register appsettings.local.json as a config
        // source, so install the exact persisted winner in memory.
        configuration[ConnectorKeyPath] = key;
    }

    /// <summary>
    /// Resolves the CLI-local <c>appsettings.local.json</c> path, placing it next
    /// to the resolved SQLite database file (the data directory). Falls back to the
    /// current working directory when the data source cannot be resolved to a path.
    /// </summary>
    internal static string ResolveLocalConfigPath(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Data Source=taskdeck.db";

        var directory = ResolveDataDirectory(connectionString);
        return Path.Combine(directory, LocalConfigFileName);
    }

    /// <summary>
    /// Resolves the configured SQLite database to a canonical file path for the
    /// first-run recovery guard. In-memory or invalid connection strings do not
    /// identify a durable database; <c>AddInfrastructure</c> remains responsible
    /// for validating those configurations.
    /// </summary>
    private static string? ResolveDatabaseFilePath(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Data Source=taskdeck.db";

        try
        {
            var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connectionString);
            if (builder.Mode == Microsoft.Data.Sqlite.SqliteOpenMode.Memory
                || builder.DataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var dataSource = string.IsNullOrWhiteSpace(builder.DataSource)
                ? "taskdeck.db"
                : builder.DataSource;
            return Path.GetFullPath(dataSource);
        }
        catch (Exception ex) when (
            ex is ArgumentException
                or PathTooLongException
                or NotSupportedException
                or IOException
                or System.Security.SecurityException)
        {
            return null;
        }
    }

    private static string ResolveDataDirectory(string connectionString)
    {
        var dataSource = ExtractDataSource(connectionString);
        if (string.IsNullOrWhiteSpace(dataSource))
        {
            return Directory.GetCurrentDirectory();
        }

        try
        {
            var fullPath = Path.GetFullPath(dataSource);
            return Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        }
        catch (Exception ex) when (
            ex is ArgumentException
                or PathTooLongException
                or NotSupportedException
                or IOException
                or System.Security.SecurityException)
        {
            // Non-file data sources (e.g. ":memory:") or otherwise unresolvable paths
            // (too long, unsupported format, restricted) must not crash startup --
            // fall back to the current working directory.
            return Directory.GetCurrentDirectory();
        }
    }

    private static string ExtractDataSource(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return string.Empty;
        }

        try
        {
            var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connectionString);
            return builder.DataSource;
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Returns the persisted connector key, generating and writing one when the
    /// file does not yet contain a key. A cross-process mutex guards the
    /// read-modify-write so that two CLI invocations racing on a fresh machine
    /// converge on a single shared key (the second reads the first's value)
    /// rather than each generating a different key.
    /// </summary>
    internal static string EnsureKeyOnDisk(
        string localConfigPath,
        string? databasePath = null,
        TimeSpan? lockTimeout = null,
        Func<string>? keyFactory = null)
    {
        using var fileLock = BootstrapFileLock.Acquire(
            localConfigPath,
            lockTimeout ?? TimeSpan.FromSeconds(10),
            onContention: () => Console.Error.WriteLine(
                "[CliFirstRun] Waiting for another Taskdeck process to finish durable local-config " +
                "initialization."));

        ExistingConfig existing;
        try
        {
            var exists = SharedConfigExistsOrThrow(localConfigPath);
            if (exists)
            {
                BootstrapFileSecurity.RestrictFileToCurrentUser(localConfigPath);
            }
            existing = ReadExisting(localConfigPath, exists);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"[CliFirstRun] Could not securely inspect {localConfigPath}; refusing to overwrite a " +
                "possibly recoverable shared connector key.", ex);
        }

        if (!string.IsNullOrWhiteSpace(existing.Key))
        {
            return existing.Key!;
        }

        if (databasePath is not null && SharedDatabaseExistsOrThrow(databasePath))
        {
            throw new InvalidOperationException(
                $"[CliFirstRun] SQLite database {databasePath} already exists but no recoverable connector " +
                "encryption key was found; refusing to generate a replacement that could make existing " +
                "connector credentials unreadable. Supply Connectors__EncryptionKey or restore " +
                "appsettings.local.json.");
        }

        var generated = (keyFactory ?? GenerateKey)();
        PersistKey(localConfigPath, existing.Root, generated);
        WarnIfSharedDbAutoGenerated(localConfigPath);
        return generated;
    }

    /// <summary>
    /// Emits a one-line stderr warning when the bootstrap AUTO-GENERATES a key
    /// while pointed at a non-default data directory (<c>TASKDECK_CONNECTION_STRING</c>
    /// set). A custom/relocated database is often shared across hosts, where
    /// separate local-config paths can produce keys that cannot decrypt each
    /// other's connector credentials -- so the operator should set one explicit
    /// shared <c>Connectors__EncryptionKey</c>.
    /// stderr only; stdout stays clean JSON.
    /// </summary>
    private static void WarnIfSharedDbAutoGenerated(string localConfigPath)
    {
        var customDb = Environment.GetEnvironmentVariable("TASKDECK_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(customDb))
        {
            return;
        }

        Console.Error.WriteLine(
            $"[CliFirstRun] WARNING: Auto-generated a new connector encryption key at " +
            $"{localConfigPath} for a custom database location. If this database is shared " +
            "across hosts (or with the API), set one explicit Connectors__EncryptionKey on " +
            "every host instead -- per-host auto-generated keys cannot decrypt each other's data.");
    }

    internal static string GenerateKey()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// Result of inspecting an existing <c>appsettings.local.json</c>.
    /// <paramref name="Root"/> is the provider-compatible object to merge into when the file is missing or
    /// valid. <paramref name="Key"/> is the existing case-insensitive connector key when present.
    /// </summary>
    private readonly record struct ExistingConfig(JsonObject Root, string? Key);

    private static bool SharedConfigExistsOrThrow(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                throw new InvalidOperationException(
                    $"[CliFirstRun] Shared local config path {path} is a directory; refusing to overwrite it.");
            }

            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new InvalidOperationException(
                $"[CliFirstRun] Could not inspect shared local config {path}; refusing to overwrite it.", ex);
        }
    }

    private static bool SharedDatabaseExistsOrThrow(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                throw new InvalidOperationException(
                    $"[CliFirstRun] Configured SQLite database path {path} is a directory; refusing to " +
                    "generate a connector encryption key.");
            }

            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new InvalidOperationException(
                $"[CliFirstRun] Could not safely inspect configured SQLite database {path}; refusing to " +
                "generate a connector encryption key.", ex);
        }
    }

    private static ExistingConfig ReadExisting(string path, bool exists)
    {
        if (!exists)
        {
            return new ExistingConfig(new JsonObject(), Key: null);
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"[CliFirstRun] Could not read shared local config {path}; refusing to overwrite it.", ex);
        }

        JsonObject root;
        try
        {
            root = JsonNode.Parse(
                    text,
                    nodeOptions: null,
                    documentOptions: new JsonDocumentOptions
                    {
                        CommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    })?.AsObject()
                ?? throw new JsonException("The shared local-config JSON root is null.");

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text), writable: false);
            _ = new ConfigurationBuilder()
                .AddJsonStream(stream)
                .Build();
        }
        catch (Exception ex) when (
            ex is JsonException or InvalidOperationException or FormatException or InvalidDataException)
        {
            throw new InvalidOperationException(
                $"[CliFirstRun] Shared local config {path} is malformed and may contain recoverable " +
                "settings or keys; refusing to overwrite it.", ex);
        }

        var flattenedPair = root.FirstOrDefault(
            pair => pair.Key.Equals(ConnectorKeyPath, StringComparison.OrdinalIgnoreCase));
        var sectionPair = root.FirstOrDefault(
            pair => pair.Key.Equals(ConnectorSection, StringComparison.OrdinalIgnoreCase));
        if (flattenedPair.Key is null
            && sectionPair.Key is not null
            && sectionPair.Value is not JsonObject)
        {
            throw new InvalidOperationException(
                $"[CliFirstRun] Shared local config {path} has a non-object Connectors section; refusing " +
                "to overwrite it.");
        }

        string? key = null;
        if (flattenedPair.Key is not null && flattenedPair.Value is JsonValue flattenedValue)
        {
            if (!flattenedValue.TryGetValue<string>(out key))
            {
                throw new InvalidOperationException(
                    $"[CliFirstRun] Shared local config {path} has a non-string connector encryption key; " +
                    "refusing to overwrite it.");
            }
        }
        else if (flattenedPair.Key is not null)
        {
            throw new InvalidOperationException(
                $"[CliFirstRun] Shared local config {path} has a null or non-scalar connector encryption key; " +
                "refusing to overwrite it.");
        }
        else
        {
            var section = sectionPair.Value as JsonObject;
            var keyPair = section?.FirstOrDefault(
                pair => pair.Key.Equals(EncryptionKeyName, StringComparison.OrdinalIgnoreCase));
            if (keyPair?.Key is not null && keyPair.Value.Value is JsonValue value)
            {
                if (!value.TryGetValue<string>(out key))
                {
                    throw new InvalidOperationException(
                        $"[CliFirstRun] Shared local config {path} has a non-string connector encryption key; " +
                        "refusing to overwrite it.");
                }
            }
            else if (keyPair?.Key is not null)
            {
                throw new InvalidOperationException(
                    $"[CliFirstRun] Shared local config {path} has a null or non-scalar connector encryption " +
                    "key; refusing to overwrite it.");
            }
        }

        return new ExistingConfig(root, key);
    }

    private static void PersistKey(string path, JsonObject root, string key)
    {
        // Merge-preserve: only set Connectors:EncryptionKey, keeping other values, the original casing, and
        // the provider-valid flattened-vs-nested representation. Creating a nested property beside an
        // existing top-level "Connectors:EncryptionKey" would make the next provider load reject a collision.
        var flattenedPair = root.FirstOrDefault(
            pair => pair.Key.Equals(ConnectorKeyPath, StringComparison.OrdinalIgnoreCase));
        if (flattenedPair.Key is not null)
        {
            root[flattenedPair.Key] = key;
            WriteConfig(path, root);
            return;
        }

        var sectionPair = root.FirstOrDefault(
            pair => pair.Key.Equals(ConnectorSection, StringComparison.OrdinalIgnoreCase));
        JsonObject sectionNode;
        if (sectionPair.Key is null)
        {
            sectionNode = new JsonObject();
            root[ConnectorSection] = sectionNode;
        }
        else if (sectionPair.Value is JsonObject existingSection)
        {
            sectionNode = existingSection;
        }
        else
        {
            throw new InvalidOperationException(
                $"[CliFirstRun] Shared local config {path} has a non-object Connectors section; refusing " +
                "to overwrite it.");
        }

        var keyPair = sectionNode.FirstOrDefault(
            pair => pair.Key.Equals(EncryptionKeyName, StringComparison.OrdinalIgnoreCase));
        sectionNode[keyPair.Key ?? EncryptionKeyName] = key;

        WriteConfig(path, root);
    }

    private static void WriteConfig(string path, JsonObject root)
    {

        var payload = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Stage into an owner-only sibling file before publishing it atomically.
        var tempDir = string.IsNullOrEmpty(dir) ? Directory.GetCurrentDirectory() : dir;
        var tempPath = Path.Combine(tempDir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            BootstrapFileSecurity.WriteRestrictedFile(tempPath, payload);
            MoveWithRetry(tempPath, path);
            BootstrapFileSecurity.VerifyFileOwnerOnly(path);
        }
        catch
        {
            try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
            throw;
        }
    }

    private static void MoveWithRetry(string tempPath, string path)
    {
        const int maxAttempts = 5;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(tempPath, path, overwrite: true);
                return;
            }
            catch (Exception ex) when (
                attempt < maxAttempts && (ex is IOException or UnauthorizedAccessException))
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(25 * attempt));
            }
        }
    }

    internal static string BuildMutexName(string path)
        => BootstrapFileLock.BuildMutexName(path);
}
