using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace Taskdeck.Api.FirstRun;

/// <summary>
/// Runs synchronously before the DI container is built.
/// Ensures that auto-generated configuration values (JWT secret, DB path)
/// are written to <c>appsettings.local.json</c> so they are available to
/// all subsequent configuration consumers.
/// </summary>
public static class FirstRunBootstrapper
{
    // Placeholder values that indicate "not configured".
    // If ANY of these appear as the JWT secret in a non-Development,
    // non-headless Production environment, startup will be blocked.
    private static readonly HashSet<string> PlaceholderSecrets =
        new(StringComparer.OrdinalIgnoreCase)
        {
            string.Empty,
            "TaskdeckDevelopmentOnlySecretKeyChangeMe123!",
            "CHANGE_ME_GENERATE_WITH_openssl_rand_base64_48"
        };

    /// <summary>
    /// Registers <c>appsettings.local.json</c> as an optional configuration
    /// source so that previously generated secrets are picked up.
    /// Call this before building <see cref="WebApplication"/>.
    /// </summary>
    /// <remarks>
    /// The source is inserted <em>before</em> any
    /// <see cref="T:Microsoft.Extensions.Configuration.EnvironmentVariables.EnvironmentVariablesConfigurationSource"/> entries so that
    /// environment variables always win over the auto-generated file.  This
    /// preserves 12-factor / container deployment patterns where operators
    /// supply <c>Jwt__SecretKey</c> (or similar) via environment variables
    /// and must not be silently overridden by a previously written file.
    /// </remarks>
    public static WebApplicationBuilder AddLocalConfigFile(this WebApplicationBuilder builder)
    {
        // A present-but-unparsable appsettings.local.json would throw the moment the configuration is built
        // (JsonConfigurationSource.Optional only suppresses a MISSING file, not a malformed one), crashing
        // startup before the first-run checks ever run. Quarantine it first: preserve the corrupt file (it
        // may hold a recoverable key) and remove the original so the optional source loads as "missing" and
        // the desktop install self-heals instead of failing to launch on every restart.
        QuarantineCorruptLocalConfig();

        var sources = builder.Configuration.Sources;

        // Find the first EnvironmentVariablesConfigurationSource so we can
        // insert the file source before it, giving env vars higher priority.
        var envIndex = -1;
        for (var i = 0; i < sources.Count; i++)
        {
            if (sources[i] is EnvironmentVariablesConfigurationSource)
            {
                envIndex = i;
                break;
            }
        }

        var fileSource = new Microsoft.Extensions.Configuration.Json.JsonConfigurationSource
        {
            Path = LocalConfigPath,
            Optional = true,
            ReloadOnChange = false
        };
        // Resolve the file provider so the source can locate the file.
        fileSource.ResolveFileProvider();

        if (envIndex >= 0)
        {
            sources.Insert(envIndex, fileSource);
        }
        else
        {
            // No env-var source found (unusual); append at end.
            sources.Add(fileSource);
        }

        return builder;
    }

    /// <summary>
    /// If the persisted local config file exists but does not parse as a JSON object, preserve it to a
    /// <c>.corrupt-*</c> sibling (it may hold a recoverable key) and remove the original, so the optional
    /// config source loads as "missing" instead of throwing at config-build time. This lets a desktop install
    /// self-heal from a corrupt <c>appsettings.local.json</c> rather than crash on every launch. Best-effort:
    /// a file that cannot be removed is reported to stderr.
    /// </summary>
    internal static void QuarantineCorruptLocalConfig() => QuarantineCorruptLocalConfigAt(LocalConfigPath);

    /// <summary>Path-parameterized core of <see cref="QuarantineCorruptLocalConfig"/> (testable seam).</summary>
    internal static void QuarantineCorruptLocalConfigAt(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            // JsonConfigurationProvider requires a JSON OBJECT at the root; AsObject() throws otherwise
            // (and Parse throws on empty/truncated/invalid content), matching what would crash config build.
            _ = JsonNode.Parse(File.ReadAllText(path))?.AsObject();
            return; // Parses as a JSON object -> usable, leave it alone.
        }
        catch (Exception ex)
        {
            PreserveCorruptConfig(path, ex);
            try
            {
                File.Delete(path);
            }
            catch (Exception delEx)
            {
                Console.Error.WriteLine(
                    $"[FirstRun] WARNING: {path} is corrupt and could not be removed ({delEx.Message}); " +
                    "startup may fail until it is deleted manually.");
            }
        }
    }

    /// <summary>
    /// Runs the first-run checks. Must be called after
    /// <see cref="AddLocalConfigFile"/> and after the standard
    /// <c>appsettings.json</c> / <c>appsettings.{env}.json</c> files have been
    /// loaded (i.e. after the builder is constructed).
    /// </summary>
    /// <remarks>
    /// <para>
    /// JWT secret generation runs in <em>all</em> environments (including
    /// Development and CI/headless) so that no hardcoded secret is required
    /// in checked-in config files.  When the secret is missing or is the
    /// well-known placeholder, a cryptographically random value is generated
    /// into <c>appsettings.local.json</c>.
    /// </para>
    /// <para>
    /// DB-path resolution and other packaged-distribution checks are still
    /// skipped in Development and in CI/headless environments.
    /// </para>
    /// </remarks>
    public static WebApplicationBuilder RunFirstRunChecks(
        this WebApplicationBuilder builder,
        ILogger logger)
    {
        // JWT secret generation runs unconditionally (including headless/CI)
        // so that no hardcoded secret is required in appsettings.Development.json.
        EnsureJwtSecret(builder.Configuration, logger);

        // Auto-generate the connector encryption key unless this is a headless Production deployment
        // (CI / cloud container). A desktop install persists the generated key to appsettings.local.json
        // next to the exe, so it is stable across restarts and the self-contained exe is runnable without
        // manually supplying Connectors__EncryptionKey. Headless Production is excluded: there the key may
        // not survive a restart, so an auto-generated one would be ephemeral and silently lose the ability
        // to decrypt stored connector credentials -- those deployments must supply a stable key, which
        // ValidateProductionSecrets enforces. See ADR-0041.
        if (ShouldAutoGenerateConnectorKey(builder.Environment.IsProduction(), IsHeadlessEnvironment()))
        {
            // In non-headless Production (the desktop exe) the key MUST persist across restarts: a failed
            // write has to be fatal, not silently degraded to an ephemeral in-memory key that the next
            // launch replaces -- which would make stored connector credentials unrecoverable.
            EnsureConnectorEncryptionKey(
                builder.Configuration,
                logger,
                requirePersistence: builder.Environment.IsProduction());
        }

        // Remaining first-run checks are for the self-hosted packaged
        // distribution only -- skip in Development and CI/headless.
        if (builder.Environment.IsDevelopment() || IsHeadlessEnvironment())
        {
            return builder;
        }

        EnsureDbPath(builder.Configuration, logger);
        return builder;
    }

    /// <summary>
    /// Validates that no placeholder JWT secret is being used in Production.
    /// Unlike <see cref="RunFirstRunChecks"/> (which auto-generates secrets for
    /// self-hosted desktop installs), this check <b>throws</b> if the configured
    /// secret is a known placeholder -- preventing cloud containers from
    /// accidentally running with an insecure or ephemeral secret.
    /// </summary>
    /// <remarks>
    /// Call this after configuration is fully built (env vars loaded).
    /// This is a hard failure by design: deploying with a placeholder JWT secret
    /// is a critical security vulnerability.
    /// </remarks>
    public static WebApplicationBuilder ValidateProductionSecrets(
        this WebApplicationBuilder builder,
        ILogger logger)
    {
        // Only enforce in Production; other environments (Development, Staging,
        // Test, etc.) use their own defaults or placeholder secrets safely.
        if (!builder.Environment.IsProduction())
        {
            return builder;
        }

        var jwtSecret = builder.Configuration["Jwt:SecretKey"] ?? string.Empty;

        if (IsPlaceholder(jwtSecret))
        {
            throw new InvalidOperationException(
                "SECURITY: The JWT secret is not configured or is a known placeholder value. " +
                "Generate a strong secret with 'openssl rand -base64 48' and set it via the " +
                "Jwt__SecretKey environment variable. The application cannot start without a " +
                "real secret in Production.");
        }

        var connectorKey = builder.Configuration["Connectors:EncryptionKey"] ?? string.Empty;

        if (string.IsNullOrWhiteSpace(connectorKey))
        {
            throw new InvalidOperationException(
                "SECURITY: The Connectors:EncryptionKey is not configured. " +
                "Generate a base64-encoded 256-bit key with 'openssl rand -base64 32' and set it via the " +
                "Connectors__EncryptionKey environment variable. The application cannot start without a " +
                $"real encryption key in Production. (If this used to run as a desktop install, an existing " +
                $"key may already be in {LocalConfigPath} -- reuse that value rather than generating a new " +
                "one, or stored connector credentials will become unrecoverable.)");
        }

        logger.LogInformation("Production secret validation passed.");
        return builder;
    }

    // -------------------------------------------------------------------------

    internal static string LocalConfigPath
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            return Path.Combine(dir, "appsettings.local.json");
        }
    }

    internal static bool IsPlaceholder(string value)
        => string.IsNullOrWhiteSpace(value) || PlaceholderSecrets.Contains(value.Trim());

    internal static string GenerateSecret()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// Decides whether to auto-generate the connector encryption key when it is not configured.
    /// Enabled everywhere EXCEPT a headless Production deployment (CI / cloud container), where a
    /// generated key may not survive a restart and would lose the ability to decrypt stored connector
    /// credentials. A non-headless Production deployment (the desktop exe) persists the key locally,
    /// so generation is safe and makes the self-contained exe runnable without a supplied key.
    /// </summary>
    internal static bool ShouldAutoGenerateConnectorKey(bool isProduction, bool isHeadless)
        => !isProduction || !isHeadless;

    /// <summary>
    /// Reads the connector encryption key directly from the persisted local config file at
    /// <paramref name="path"/>, bypassing configuration-source precedence. This lets the bootstrapper
    /// detect a key that a higher-priority empty/whitespace source (e.g. an empty
    /// <c>Connectors__EncryptionKey</c> environment variable) is masking, so it can be REUSED rather than
    /// overwritten by a freshly generated one. Returns <c>true</c> only when the file holds a non-empty key;
    /// returns <c>false</c> for a missing, unparsable, non-object, or empty value (a corrupt file is handled
    /// separately by <see cref="QuarantineCorruptLocalConfig"/>).
    /// </summary>
    internal static bool TryReadPersistedConnectorKey(string path, out string? key)
    {
        key = null;
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var node = JsonNode.Parse(File.ReadAllText(path))?.AsObject();
            key = node?["Connectors"]?.AsObject()?["EncryptionKey"]?.GetValue<string>();
            return !string.IsNullOrWhiteSpace(key);
        }
        catch
        {
            // Missing/unparsable/non-object/non-string: treat as "no recoverable key persisted".
            key = null;
            return false;
        }
    }

    private static void EnsureJwtSecret(IConfiguration configuration, ILogger logger)
    {
        var configured = configuration["Jwt:SecretKey"] ?? string.Empty;

        if (!IsPlaceholder(configured))
        {
            return;
        }

        var generated = GenerateSecret();
        try
        {
            PersistValue("Jwt", "SecretKey", generated);
            // Reload so subsequent configuration reads get the new value.
            if (configuration is IConfigurationRoot root)
            {
                root.Reload();
            }

            logger.LogInformation(
                "First-run: JWT secret was not configured. A random secret has been " +
                "generated and saved to {ConfigFile}.", LocalConfigPath);
        }
        catch (IOException ex)
        {
            // File may be locked by another process (e.g. parallel test
            // factories sharing the same output directory).  Fall back to
            // setting the value in-memory so the current startup still
            // succeeds.
            configuration["Jwt:SecretKey"] = generated;
            logger.LogWarning(
                "First-run: Could not persist JWT secret to {ConfigFile} ({Error}). " +
                "A transient in-memory secret has been generated instead.",
                LocalConfigPath, ex.Message);
        }
    }

    private static void EnsureConnectorEncryptionKey(
        IConfiguration configuration,
        ILogger logger,
        bool requirePersistence = false)
    {
        var configured = configuration["Connectors:EncryptionKey"] ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return;
        }

        // The effective value is empty. Before generating a new key (which PersistValue would write OVER the
        // file in place, destroying any key already there), check whether a key is already persisted on disk.
        // If so, a higher-priority empty/whitespace source -- most likely an empty Connectors__EncryptionKey
        // environment variable -- is masking it. REUSE the persisted key instead of generating: regenerating
        // would permanently destroy the only copy of the key the stored connector credentials were encrypted
        // with. Reuse is stable across restarts (the next launch reads the same persisted key) and keeps the
        // app working despite the misconfigured empty variable.
        if (TryReadPersistedConnectorKey(LocalConfigPath, out var persisted))
        {
            configuration["Connectors:EncryptionKey"] = persisted;
            logger.LogWarning(
                "First-run: A higher-priority configuration source (likely an empty Connectors__EncryptionKey " +
                "environment variable) is masking the connector key persisted in {ConfigFile}. Reusing the " +
                "persisted key so stored connector credentials stay decryptable; unset the empty variable to " +
                "silence this warning.", LocalConfigPath);
            return;
        }

        var generated = GenerateSecret();
        try
        {
            PersistValue("Connectors", "EncryptionKey", generated);
            if (configuration is IConfigurationRoot root)
            {
                root.Reload();
            }

            logger.LogInformation(
                "First-run: Connector encryption key was not configured. A random key has been generated " +
                "and saved to {ConfigFile}. BACK UP THIS FILE alongside your database -- it is required to " +
                "decrypt stored connector credentials; losing it makes them unrecoverable.", LocalConfigPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // PersistValue can throw UnauthorizedAccessException (e.g. a read-only install dir such as
            // Program Files) as well as IOException (locked file, disk full, lock unavailable).
            if (requirePersistence)
            {
                // A desktop (non-headless Production) install must persist the key. An in-memory key would
                // be lost on the next launch, which would then generate a different one and silently lose
                // the ability to decrypt previously-stored connector credentials. Fail loudly instead.
                throw new InvalidOperationException(
                    $"First-run: Could not persist the connector encryption key to {LocalConfigPath} " +
                    $"({ex.Message}). A run-once in-memory key would be lost on restart and make stored " +
                    "connector credentials unrecoverable. Ensure the application directory is writable, or " +
                    "set a stable key via the Connectors__EncryptionKey environment variable.", ex);
            }

            // Non-Production (dev/staging/test): a transient in-memory key is acceptable -- these do not
            // carry credentials that must survive a restart, and parallel test harnesses can lock the file.
            configuration["Connectors:EncryptionKey"] = generated;
            logger.LogWarning(
                "First-run: Could not persist connector encryption key to {ConfigFile} ({Error}). " +
                "A transient in-memory key has been generated instead.",
                LocalConfigPath, ex.Message);
            return;
        }

        // The key is now persisted on disk. Make sure it is also the effective in-process value: a reload may
        // not propagate through every provider (test harnesses), and an empty higher-priority source could
        // mask the freshly written file. Either way the key IS persisted and recoverable -- the next launch
        // reads it back (and reuses it via TryReadPersistedConnectorKey if still masked) -- so an in-memory
        // value here is safe (no data loss), unlike overwriting an existing key would have been.
        if (string.IsNullOrWhiteSpace(configuration["Connectors:EncryptionKey"]))
        {
            configuration["Connectors:EncryptionKey"] = generated;
            if (requirePersistence)
            {
                // In Production the only way the just-persisted key is not the effective value is a
                // higher-priority empty source masking it. The key is safe on disk and will be reused on the
                // next launch; surface the misconfiguration so the operator can clear it.
                logger.LogWarning(
                    "First-run: The connector key was persisted to {ConfigFile} but a higher-priority " +
                    "configuration source (likely an empty Connectors__EncryptionKey environment variable) is " +
                    "masking it. The persisted key will be reused on the next launch; unset the empty variable.",
                    LocalConfigPath);
            }
        }
    }

    private static void EnsureDbPath(IConfiguration configuration, ILogger logger)
    {
        var resolveAppData = configuration.GetValue<bool?>("FirstRun:ResolveAppDataDbPath") ?? true;
        if (!resolveAppData)
        {
            return;
        }

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? string.Empty;
        var dataSource = ExtractDataSource(connectionString);

        // Already an absolute path — nothing to do.
        if (!string.IsNullOrWhiteSpace(dataSource) && Path.IsPathRooted(dataSource))
        {
            return;
        }

        var appDataDir = GetAppDataPath();
        Directory.CreateDirectory(appDataDir);

        var dbFile = string.IsNullOrWhiteSpace(dataSource)
            ? "taskdeck.db"
            : Path.GetFileName(dataSource);

        var resolvedPath = Path.Combine(appDataDir, dbFile);
        var resolvedConnectionString = $"Data Source={resolvedPath}";

        // Write into the local config file so the value is picked up by
        // AddInfrastructure later in the startup pipeline.
        try
        {
            PersistValue("ConnectionStrings", "DefaultConnection", resolvedConnectionString);
        }
        catch (IOException ex)
        {
            // The cross-process bootstrap lock may be unavailable on locked-down
            // hosts. Fall back to setting the value in-memory so the current
            // startup still succeeds with a relative DB path.
            configuration["ConnectionStrings:DefaultConnection"] = resolvedConnectionString;
            logger.LogWarning(
                "First-run: Could not persist DB path to {ConfigFile} ({Error}). " +
                "A transient in-memory connection string has been set instead.",
                LocalConfigPath, ex.Message);
            return;
        }

        if (configuration is IConfigurationRoot root)
        {
            root.Reload();
        }

        logger.LogInformation(
            "First-run: SQLite DB path resolved to AppData location: {DbPath}", resolvedPath);
    }

    internal static bool IsHeadlessEnvironment()
    {
        return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TF_BUILD"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TASKDECK_HEADLESS"));
    }

    internal static string GetAppDataPath()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create);
        return Path.Combine(localAppData, "Taskdeck");
    }

    private static string ExtractDataSource(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return string.Empty;

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
    /// Backs up an unparsable config file to a timestamped <c>.corrupt-*</c> sibling before it is rewritten,
    /// so a previously-generated secret it may still hold (the connector key in particular) is recoverable
    /// by an operator instead of being silently overwritten. Best-effort: failure to copy is logged, not fatal.
    /// </summary>
    private static void PreserveCorruptConfig(string path, Exception parseError)
    {
        var backupPath = $"{path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
        try
        {
            File.Copy(path, backupPath, overwrite: false);
            Console.Error.WriteLine(
                $"[FirstRun] WARNING: {path} contains invalid JSON ({parseError.Message}). A copy was " +
                $"preserved at {backupPath} for recovery -- it may hold a previously-generated key -- and the " +
                "file will be rewritten.");
        }
        catch (Exception copyEx)
        {
            Console.Error.WriteLine(
                $"[FirstRun] WARNING: {path} contains invalid JSON ({parseError.Message}) and could NOT be " +
                $"backed up ({copyEx.Message}); it will be overwritten.");
        }
    }

    private static void PersistValue(string section, string key, string value)
    {
        var path = LocalConfigPath;

        // Cross-process mutex: multiple xUnit test processes (and any
        // accidentally concurrent startup in the same output directory) must
        // not race on the read-modify-write of appsettings.local.json.  Two
        // concurrent File.WriteAllText calls can interleave and leave the
        // file with trailing bytes from the longer write after the shorter
        // one finishes, producing the `'}' is invalid after a single JSON
        // value` parse error seen in CI.
        var mutexName = BuildMutexName(path);
        Mutex? mutex = null;
        var acquired = false;
        try
        {
            try
            {
                // The named (Global\ on Windows) mutex ctor AND WaitOne can throw on
                // locked-down or multi-user hosts -- e.g. when another user already
                // owns the name. That must NOT crash API startup.
                mutex = new System.Threading.Mutex(initiallyOwned: false, name: mutexName);
                acquired = mutex.WaitOne(TimeSpan.FromSeconds(10));
            }
            catch (AbandonedMutexException)
            {
                // A previous holder crashed without releasing; we still own it now.
                acquired = true;
            }
            catch (Exception ex) when (
                ex is UnauthorizedAccessException
                    or WaitHandleCannotBeOpenedException
                    or IOException)
            {
                // Could not create/open or wait on the cross-process lock. Degrade
                // gracefully: skip the persistent write entirely so two unsynchronized
                // writers never race to persist different keys. The caller's catch
                // block will fall back to an in-memory value.
                Console.Error.WriteLine(
                    $"[FirstRun] WARNING: Could not acquire the cross-process bootstrap " +
                    $"lock ({ex.Message}). Skipping persistent write.");
                throw new IOException(
                    "Cross-process bootstrap lock unavailable; skipping persistent write.", ex);
            }

            JsonObject root;
            if (File.Exists(path))
            {
                try
                {
                    var existing = File.ReadAllText(path);
                    root = JsonNode.Parse(existing)?.AsObject() ?? new JsonObject();
                }
                catch (Exception ex)
                {
                    // The file is unparsable (e.g. an interrupted write or a hand-edit). It may still hold
                    // the ONLY copy of a previously-generated secret -- losing the connector key in
                    // particular orphans stored connector credentials -- so preserve it for operator recovery
                    // before starting fresh, rather than silently overwriting it. Logger is not available at
                    // this pre-DI stage, so warn to stderr.
                    PreserveCorruptConfig(path, ex);
                    root = new JsonObject();
                }
            }
            else
            {
                root = new JsonObject();
            }

            if (root[section] is not JsonObject sectionNode)
            {
                sectionNode = new JsonObject();
                root[section] = sectionNode;
            }

            sectionNode[key] = value;

            var options = new JsonSerializerOptions { WriteIndented = true };
            var payload = root.ToJsonString(options);

            // Atomic write: stage into a sibling temp file then move into place.
            // File.WriteAllText is not atomic — a concurrent reader or writer
            // can observe a partially written file.  A rename onto an existing
            // path is atomic on both Windows and Linux file systems we target.
            var dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);
            var tempPath = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

            if (!OperatingSystem.IsWindows())
            {
                // Create the file empty and restrict to 0600 BEFORE writing the payload,
                // eliminating the TOCTOU window where secrets would be world-readable
                // under the default umask (022).
                File.Create(tempPath).Dispose();
                File.SetUnixFileMode(tempPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                File.WriteAllText(tempPath, payload);
            }
            else
            {
                File.WriteAllText(tempPath, payload);
            }

            try
            {
                ReplaceFileWithRetry(tempPath, path);
            }
            catch
            {
                // Best-effort cleanup; the main write path's exception will propagate.
                try { File.Delete(tempPath); } catch { /* ignore */ }
                throw;
            }
        }
        finally
        {
            if (acquired)
            {
                try { mutex?.ReleaseMutex(); }
                catch (ApplicationException) { }
            }
            mutex?.Dispose();
        }
    }

    private static void ReplaceFileWithRetry(string tempPath, string path)
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
                attempt < maxAttempts &&
                (ex is IOException || ex is UnauthorizedAccessException))
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(25 * attempt));
            }
        }
    }

    internal static string BuildMutexName(string path)
    {
        // Named mutex must be stable per-path and fit OS name rules.
        // Use SHA256 of the absolute path; prefix with "Global\" on Windows
        // so different user sessions on the same machine still coordinate.
        var normalized = Path.GetFullPath(path).ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        var hex = Convert.ToHexString(hash).AsSpan(0, 32);
        var prefix = OperatingSystem.IsWindows() ? "Global\\" : string.Empty;
        return $"{prefix}Taskdeck.FirstRun.{hex}";
    }
}
