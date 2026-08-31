using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Taskdeck.Application.Connectors;
using Taskdeck.Infrastructure.Services;

namespace Taskdeck.Cli.Commands;

/// <summary>
/// Read-only operator check for connector credentials in a restored database.
/// This command deliberately runs before host construction so it cannot migrate,
/// bootstrap a key, or start provider clients.
/// </summary>
internal static class ConnectorVerificationCommand
{
    private const string Usage =
        "taskdeck --verify-connectors --database <path> [--key-file <path>]";

    public static bool IsRequest(IReadOnlyList<string>? args) =>
        args is { Count: > 0 } &&
        string.Equals(args[0], "--verify-connectors", StringComparison.OrdinalIgnoreCase);

    public static async Task<int> ExecuteAsync(IReadOnlyList<string> args)
    {
        if (!TryParseArguments(args, out var databaseArgument, out var keyFileArgument, out var usageError))
        {
            return ConsoleOutput.PrintUsageError(usageError, Usage);
        }

        if (!TryResolveExistingPath(databaseArgument!, out var databasePath) ||
            !File.Exists(databasePath))
        {
            return ConsoleOutput.PrintFailure(
                "CONNECTOR_DATABASE_UNAVAILABLE",
                "Connector database is unavailable.");
        }

        var keyResult = await ReadKeyAsync(keyFileArgument);
        if (!keyResult.IsAvailable)
        {
            return ConsoleOutput.PrintFailure(keyResult.ErrorCode!, keyResult.ErrorMessage!);
        }

        ICredentialEncryptionService encryptionService;
        try
        {
            encryptionService = new AesCredentialEncryptionService(keyResult.Value!);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return ConsoleOutput.PrintFailure(
                "CONNECTOR_KEY_INVALID",
                "Connector encryption key is invalid.");
        }

        ConnectorVerificationCounts counts;
        try
        {
            counts = await VerifyDatabaseAsync(databasePath!, encryptionService);
        }
        catch (Exception)
        {
            // This is the process boundary. Do not print exception text, paths,
            // ciphertext, or identifiers from a damaged/restored database.
            return ConsoleOutput.PrintFailure(
                "CONNECTOR_DATABASE_UNAVAILABLE",
                "Connector database is unavailable.");
        }

        Console.WriteLine($"ok={counts.Ok} failed={counts.Failed}");
        if (counts.Total == 0)
        {
            Console.WriteLine("Nothing to verify.");
        }

        return counts.Failed == 0 ? ExitCodes.Success : ExitCodes.Failure;
    }

    private static bool TryParseArguments(
        IReadOnlyList<string> args,
        out string? database,
        out string? keyFile,
        out string usageError)
    {
        database = null;
        keyFile = null;
        usageError = string.Empty;

        if (!IsRequest(args))
        {
            usageError = "Connector verification requires --verify-connectors as the first argument.";
            return false;
        }

        for (var index = 1; index < args.Count; index += 2)
        {
            if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                usageError = $"Option {args[index]} requires a value.";
                return false;
            }

            var value = args[index + 1];
            if (string.Equals(args[index], "--database", StringComparison.OrdinalIgnoreCase))
            {
                if (database is not null)
                {
                    usageError = "Option --database may be supplied only once.";
                    return false;
                }

                database = value;
                continue;
            }

            if (string.Equals(args[index], "--key-file", StringComparison.OrdinalIgnoreCase))
            {
                if (keyFile is not null)
                {
                    usageError = "Option --key-file may be supplied only once.";
                    return false;
                }

                keyFile = value;
                continue;
            }

            usageError = $"Unknown connector verification option: {args[index]}.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(database))
        {
            usageError = "Connector verification requires --database <path>.";
            return false;
        }

        return true;
    }

    private static async Task<ConnectorKeyResult> ReadKeyAsync(string? keyFileArgument)
    {
        if (!string.IsNullOrWhiteSpace(keyFileArgument))
        {
            if (!TryResolveExistingPath(keyFileArgument, out var keyPath) || !File.Exists(keyPath))
            {
                return ConnectorKeyResult.Failure(
                    "CONNECTOR_KEY_UNAVAILABLE",
                    "Connector encryption key file is unavailable.");
            }

            try
            {
                var value = (await File.ReadAllTextAsync(keyPath!)).Trim();
                return string.IsNullOrWhiteSpace(value)
                    ? ConnectorKeyResult.Failure(
                        "CONNECTOR_KEY_INVALID",
                        "Connector encryption key is invalid.")
                    : ConnectorKeyResult.Success(value);
            }
            catch (Exception)
            {
                return ConnectorKeyResult.Failure(
                    "CONNECTOR_KEY_UNAVAILABLE",
                    "Connector encryption key file is unavailable.");
            }
        }

        var environmentKey = Environment.GetEnvironmentVariable("TASKDECK_CONNECTORS__ENCRYPTIONKEY")
            ?? Environment.GetEnvironmentVariable("Connectors__EncryptionKey");
        return string.IsNullOrWhiteSpace(environmentKey)
            ? ConnectorKeyResult.Failure(
                "CONNECTOR_KEY_MISSING",
                "Connector encryption key was not supplied.")
            : ConnectorKeyResult.Success(environmentKey.Trim());
    }

    private static bool TryResolveExistingPath(string path, out string? fullPath)
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

    private static async Task<ConnectorVerificationCounts> VerifyDatabaseAsync(
        string databasePath,
        ICredentialEncryptionService encryptionService)
    {
        EnsureNoJournalSidecars(databasePath);
        var connectionString = CreateReadOnlyConnectionString(databasePath);

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"EncryptedValue\" FROM \"ConnectorCredentials\";";
        await using var reader = await command.ExecuteReaderAsync();

        var verifier = new ConnectorCredentialVerifier(encryptionService);
        while (await reader.ReadAsync())
        {
            var ciphertext = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            verifier.Verify(ciphertext);
        }

        EnsureNoJournalSidecars(databasePath);
        return verifier.Counts;
    }

    internal static string CreateReadOnlyConnectionString(string databasePath)
    {
        var dataSource = new UriBuilder
        {
            Scheme = Uri.UriSchemeFile,
            Host = string.Empty,
            Path = Path.GetFullPath(databasePath),
            Query = "immutable=1"
        }.Uri.AbsoluteUri;

        return new SqliteConnectionStringBuilder
        {
            DataSource = dataSource,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString();
    }

    private static void EnsureNoJournalSidecars(string databasePath)
    {
        if (File.Exists($"{databasePath}-wal") ||
            File.Exists($"{databasePath}-shm") ||
            File.Exists($"{databasePath}-journal"))
        {
            throw new InvalidOperationException(
                "Connector verification requires a standalone database without journal sidecars.");
        }
    }

    private sealed record ConnectorKeyResult(
        bool IsAvailable,
        string? Value,
        string? ErrorCode,
        string? ErrorMessage)
    {
        public static ConnectorKeyResult Success(string value) => new(true, value, null, null);

        public static ConnectorKeyResult Failure(string code, string message) =>
            new(false, null, code, message);
    }
}

internal sealed class ConnectorCredentialVerifier
{
    private readonly ICredentialEncryptionService _encryptionService;
    private long _ok;
    private long _failed;

    public ConnectorCredentialVerifier(ICredentialEncryptionService encryptionService)
    {
        ArgumentNullException.ThrowIfNull(encryptionService);
        _encryptionService = encryptionService;
    }

    public ConnectorVerificationCounts Counts => new(_ok, _failed);

    public void Verify(string ciphertext)
    {
        try
        {
            _ = _encryptionService.Decrypt(ciphertext);
            _ok = checked(_ok + 1);
        }
        catch (Exception exception) when (
            exception is ArgumentException or FormatException or CryptographicException)
        {
            _failed = checked(_failed + 1);
        }
    }
}

internal sealed record ConnectorVerificationCounts(long Ok, long Failed)
{
    public long Total => checked(Ok + Failed);
}
