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
/// It intentionally does NOT modify the shared <c>AddInfrastructure</c>: the API
/// deliberately fail-fasts on a missing key in Production. NOTE the key location
/// differs from the API's <c>FirstRunBootstrapper</c>, which persists ITS key
/// next to the executable (<c>AppContext.BaseDirectory</c>) -- this CLI bootstrap
/// writes next to the data directory. Because the two locations differ, a
/// deployment that points BOTH the API and CLI (or several hosts) at one shared
/// database would auto-generate a DIFFERENT key per host and be unable to decrypt
/// the other's connector credentials. For any shared-database or multi-host
/// deployment, set one explicit <c>Connectors__EncryptionKey</c> (env var or
/// appsettings) on every host instead of relying on this per-host
/// auto-generation; the bootstrap emits a stderr warning when it auto-generates a
/// key against a non-default data directory (<c>TASKDECK_CONNECTION_STRING</c>
/// set) to surface this.
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
    internal static string EnsureKeyOnDisk(string localConfigPath)
    {
        var mutexName = BuildMutexName(localConfigPath);
        Mutex? mutex = null;
        var acquired = false;
        try
        {
            try
            {
                // The named (Global\ on Windows) mutex ctor AND WaitOne can throw on
                // locked-down or multi-user hosts -- e.g. when another user already
                // owns the name. That must NOT crash the CLI at startup (the exact
                // failure this bootstrap exists to prevent).
                mutex = new Mutex(initiallyOwned: false, name: mutexName);
                acquired = mutex.WaitOne(TimeSpan.FromSeconds(10));
            }
            catch (AbandonedMutexException)
            {
                // A previous holder crashed without releasing; we own it now.
                acquired = true;
            }
            catch (Exception ex) when (
                ex is UnauthorizedAccessException
                    or WaitHandleCannotBeOpenedException
                    or IOException)
            {
                // Could not create/open or wait on the cross-process lock. Degrade
                // gracefully: continue WITHOUT the lock and treat this run as
                // read-only (see the !acquired branch below) so two unsynchronized
                // writers never race to persist different keys.
                Console.Error.WriteLine(
                    $"[CliFirstRun] WARNING: Could not acquire the cross-process bootstrap " +
                    $"lock ({ex.Message}). Proceeding without it; a generated key will not be " +
                    "persisted on this run.");
            }

            // Re-check (inside the lock when held): a racing process may have just
            // written a key, or one may already exist from a previous run. Reading
            // is safe without the lock.
            var existing = ReadExisting(localConfigPath);
            if (!string.IsNullOrWhiteSpace(existing.Key))
            {
                return existing.Key!;
            }

            var generated = GenerateKey();

            if (!acquired)
            {
                // We do NOT hold the lock (ctor/wait threw, or the 10s wait timed
                // out). Persisting now could race a concurrent writer and leave
                // connector credentials encrypted under a key no longer on disk.
                // Use a transient in-memory key for this run only; a later run that
                // wins the lock will persist a stable key. Warn on stderr (stdout
                // stays clean JSON).
                Console.Error.WriteLine(
                    "[CliFirstRun] WARNING: Bootstrap lock unavailable; using a transient " +
                    "in-memory connector encryption key for this run without persisting it. " +
                    "Set an explicit Connectors__EncryptionKey to avoid per-run keys.");
                return generated;
            }

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
                WarnIfSharedDbAutoGenerated(localConfigPath);
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
            if (acquired && mutex is not null)
            {
                try
                {
                    mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // Best-effort release: another path may already have released it.
                }
            }

            mutex?.Dispose();
        }
    }

    /// <summary>
    /// Emits a one-line stderr warning when the bootstrap AUTO-GENERATES a key
    /// while pointed at a non-default data directory (<c>TASKDECK_CONNECTION_STRING</c>
    /// set). A custom/relocated database is often shared across hosts, where
    /// per-host auto-generated keys diverge (the API stores its key next to the
    /// executable) and cannot decrypt each other's connector credentials -- so the
    /// operator should set one explicit shared <c>Connectors__EncryptionKey</c>.
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

        if (!OperatingSystem.IsWindows())
        {
            // The payload is a base64 256-bit connector encryption key. On a default
            // POSIX umask (022), File.WriteAllText creates the temp file 0644
            // (world-readable), and File.Move preserves that mode -- exposing the key
            // to other local users. Restrict to owner read/write (0600) BEFORE the
            // move so the final file is never world-readable. No-op on Windows, where
            // NTFS ACL inheritance governs access.
            File.SetUnixFileMode(tempPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

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
