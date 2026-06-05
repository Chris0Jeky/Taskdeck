using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace Taskdeck.Cli;

/// <summary>
/// First-run bootstrap for the standalone CLI.
///
/// The CLI ships no <c>appsettings.json</c> and never runs the API's
/// <c>FirstRunBootstrapper</c>, yet <c>AddInfrastructure</c> fail-fasts when
/// <c>Connectors:EncryptionKey</c> is missing. On a clean machine that made
/// <c>taskdeck boards list</c> crash at startup unless the operator exported
/// <c>TASKDECK_CONNECTORS__ENCRYPTIONKEY</c> by hand.
///
/// This helper provisions a cryptographically-random 256-bit connector key on
/// first run, persists it to a CLI-local <c>appsettings.local.json</c> (next to
/// the resolved SQLite data directory, so the key is co-located with the
/// encrypted data it protects and lives in a user-writable location), and loads
/// it into configuration so <c>AddInfrastructure</c> succeeds.
///
/// It intentionally does NOT modify the shared <c>AddInfrastructure</c>: the API
/// deliberately fail-fasts on a missing key in Production. This is a CLI-local
/// convenience that mirrors the API's <c>FirstRunBootstrapper</c> write pattern.
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
    /// No-op when a key is already supplied (environment variable, appsettings,
    /// or a previously generated <c>appsettings.local.json</c>).
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
        var key = EnsureKeyOnDisk(localConfigPath);

        // Load the key into the live configuration so AddInfrastructure can read
        // it. The CLI host does not register appsettings.local.json as a config
        // source, so set it in-memory (this is also the persist-failure fallback).
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
        catch (ArgumentException)
        {
            // Non-file data sources (e.g. ":memory:") have no real directory.
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
    internal static string EnsureKeyOnDisk(string localConfigPath)
    {
        var mutexName = BuildMutexName(localConfigPath);
        using var mutex = new Mutex(initiallyOwned: false, name: mutexName);
        var acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(TimeSpan.FromSeconds(10));
            }
            catch (AbandonedMutexException)
            {
                // A previous holder crashed without releasing; we own it now.
                acquired = true;
            }

            // Re-check inside the mutex: a racing process may have just written one.
            var existing = ReadExisting(localConfigPath);
            if (!string.IsNullOrWhiteSpace(existing.Key))
            {
                return existing.Key!;
            }

            var generated = GenerateKey();

            if (existing.PreserveFile)
            {
                // An existing file could not be read (e.g. transiently locked by an
                // editor/AV/backup). Do NOT overwrite it -- a valid key may be on
                // disk, and clobbering it would make previously-encrypted connector
                // credentials undecryptable. Use a transient key for this run; the
                // next run will pick up the real key.
                Console.Error.WriteLine(
                    $"[CliFirstRun] WARNING: Could not read {localConfigPath} to load the " +
                    "connector encryption key. Using a transient in-memory key for this run.");
                return generated;
            }

            try
            {
                PersistKey(localConfigPath, existing.Root, generated);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Could not persist (read-only dir, lock contention). The current
                // invocation still works with a transient in-memory key; warn on
                // stderr so stdout stays clean JSON.
                Console.Error.WriteLine(
                    $"[CliFirstRun] WARNING: Could not persist connector encryption key to " +
                    $"{localConfigPath} ({ex.Message}). Using a transient in-memory key for this run.");
            }

            return generated;
        }
        finally
        {
            if (acquired)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    internal static string GenerateKey()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// Result of inspecting an existing <c>appsettings.local.json</c>.
    /// <paramref name="Root"/> is the object to merge new values into (empty when
    /// the file is missing or corrupt). <paramref name="Key"/> is the existing
    /// connector key when present and a string. <paramref name="PreserveFile"/>
    /// is true when the file exists but could not be read, signalling the caller
    /// to avoid overwriting it.
    /// </summary>
    private readonly record struct ExistingConfig(JsonObject Root, string? Key, bool PreserveFile);

    private static ExistingConfig ReadExisting(string path)
    {
        if (!File.Exists(path))
        {
            return new ExistingConfig(new JsonObject(), Key: null, PreserveFile: false);
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // File exists but is temporarily unreadable -- preserve it rather than
            // risk clobbering a valid key.
            return new ExistingConfig(new JsonObject(), Key: null, PreserveFile: true);
        }

        JsonObject root;
        try
        {
            root = JsonNode.Parse(text)?.AsObject() ?? new JsonObject();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // No logger at this pre-DI stage. Warn on stderr and start fresh rather
            // than silently discarding the corrupt file.
            Console.Error.WriteLine(
                $"[CliFirstRun] WARNING: {path} contains invalid JSON and will be overwritten. " +
                $"Details: {ex.Message}");
            return new ExistingConfig(new JsonObject(), Key: null, PreserveFile: false);
        }

        // Type-safe extraction: a non-string EncryptionKey value (e.g. a number)
        // must not throw -- treat it as absent so a valid key is regenerated.
        var key = (root[ConnectorSection] as JsonObject)?[EncryptionKeyName] is JsonValue value
            && value.TryGetValue<string>(out var keyText)
                ? keyText
                : null;

        return new ExistingConfig(root, key, PreserveFile: false);
    }

    private static void PersistKey(string path, JsonObject root, string key)
    {
        // Merge-preserve: only set Connectors:EncryptionKey, keeping any other keys.
        if (root[ConnectorSection] is not JsonObject sectionNode)
        {
            sectionNode = new JsonObject();
            root[ConnectorSection] = sectionNode;
        }

        sectionNode[EncryptionKeyName] = key;

        var payload = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Atomic write: stage into a sibling temp file, then move into place.
        // File.WriteAllText is not atomic; a concurrent reader could otherwise
        // observe a partially written file.
        var tempDir = string.IsNullOrEmpty(dir) ? Directory.GetCurrentDirectory() : dir;
        var tempPath = Path.Combine(tempDir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(tempPath, payload);
        try
        {
            MoveWithRetry(tempPath, path);
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

    private static string BuildMutexName(string path)
    {
        // Stable per-path name within OS naming rules. SHA256 of the absolute path;
        // prefix with "Global\" on Windows so different user sessions coordinate.
        var normalized = Path.GetFullPath(path).ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        var hex = Convert.ToHexString(hash).AsSpan(0, 32);
        var prefix = OperatingSystem.IsWindows() ? "Global\\" : string.Empty;
        return $"{prefix}Taskdeck.Cli.FirstRun.{hex}";
    }
}
