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

        // Connector key auto-generation is skipped in Production to avoid
        // ephemeral keys that cause data loss on container restart.
        // Production must supply a stable key; ValidateProductionSecrets enforces this.
        if (!builder.Environment.IsProduction())
        {
            EnsureConnectorEncryptionKey(builder.Configuration, logger);
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
                "real encryption key in Production.");
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

    private static void EnsureConnectorEncryptionKey(IConfiguration configuration, ILogger logger)
    {
        var configured = configuration["Connectors:EncryptionKey"] ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(configured))
        {
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
                "First-run: Connector encryption key was not configured. A random key has been " +
                "generated and saved to {ConfigFile}.", LocalConfigPath);
        }
        catch (IOException ex)
        {
            logger.LogWarning(
                "First-run: Could not persist connector encryption key to {ConfigFile} ({Error}). " +
                "A transient in-memory key has been generated instead.",
                LocalConfigPath, ex.Message);
        }

        // Always set in-memory as a fallback: the file-based reload may not
        // propagate through all configuration providers (e.g. in test harnesses).
        if (string.IsNullOrWhiteSpace(configuration["Connectors:EncryptionKey"]))
        {
            configuration["Connectors:EncryptionKey"] = generated;
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
        PersistValue("ConnectionStrings", "DefaultConnection", resolvedConnectionString);

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
        using var mutex = new System.Threading.Mutex(initiallyOwned: false, name: mutexName);
        var acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(TimeSpan.FromSeconds(10));
            }
            catch (AbandonedMutexException)
            {
                // A previous holder crashed without releasing; we still own it now.
                acquired = true;
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
                    // Logger is not available at this pre-DI stage; warn to stderr and start fresh
                    // rather than silently discarding the corrupt file.
                    Console.Error.WriteLine(
                        $"[FirstRun] WARNING: {path} contains invalid JSON and will be overwritten. " +
                        $"Details: {ex.Message}");
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
            File.WriteAllText(tempPath, payload);
            try
            {
                File.Move(tempPath, path, overwrite: true);
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
                mutex.ReleaseMutex();
            }
        }
    }

    private static string BuildMutexName(string path)
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
