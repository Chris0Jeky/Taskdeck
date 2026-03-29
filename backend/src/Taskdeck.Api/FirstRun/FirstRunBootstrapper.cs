using System.Security.Cryptography;
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
    // Placeholder values that indicate "not configured"
    private static readonly HashSet<string> PlaceholderSecrets =
        new(StringComparer.OrdinalIgnoreCase)
        {
            string.Empty,
            "TaskdeckDevelopmentOnlySecretKeyChangeMe123!"
        };

    /// <summary>
    /// Registers <c>appsettings.local.json</c> as an optional configuration
    /// source so that previously generated secrets are picked up.
    /// Call this before building <see cref="WebApplication"/>.
    /// </summary>
    /// <remarks>
    /// The source is inserted <em>before</em> any
    /// <see cref="EnvironmentVariablesConfigurationSource"/> entries so that
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
    /// Checks are skipped in Development and in CI/headless environments.
    /// They are intended for the packaged self-hosted production scenario.
    /// </remarks>
    public static WebApplicationBuilder RunFirstRunChecks(
        this WebApplicationBuilder builder,
        ILogger logger)
    {
        // First-run checks are for the self-hosted packaged distribution.
        // In Development the developer supplies their own config values.
        if (builder.Environment.IsDevelopment())
        {
            return builder;
        }

        // Also skip in CI / automated environments.
        if (IsHeadlessEnvironment())
        {
            return builder;
        }

        EnsureJwtSecret(builder.Configuration, logger);
        EnsureDbPath(builder.Configuration, logger);
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
        PersistConnectionString(resolvedConnectionString);

        if (configuration is IConfigurationRoot root)
        {
            root.Reload();
        }

        logger.LogInformation(
            "First-run: SQLite DB path resolved to AppData location: {DbPath}", resolvedPath);
    }

    private static bool IsHeadlessEnvironment()
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
        {
            return string.Empty;
        }

        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=', StringComparison.Ordinal);
            if (eq < 0)
            {
                continue;
            }

            var key = part[..eq].Trim();
            if (key.Equals("Data Source", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("DataSource", StringComparison.OrdinalIgnoreCase))
            {
                return part[(eq + 1)..].Trim();
            }
        }

        return string.Empty;
    }

    private static void PersistConnectionString(string connectionString)
    {
        var path = LocalConfigPath;

        JsonObject root;
        if (File.Exists(path))
        {
            try
            {
                var existing = File.ReadAllText(path);
                root = JsonNode.Parse(existing)?.AsObject() ?? new JsonObject();
            }
            catch
            {
                root = new JsonObject();
            }
        }
        else
        {
            root = new JsonObject();
        }

        if (root["ConnectionStrings"] is not JsonObject connStrings)
        {
            connStrings = new JsonObject();
            root["ConnectionStrings"] = connStrings;
        }

        connStrings["DefaultConnection"] = connectionString;

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, root.ToJsonString(options));
    }

    private static void PersistValue(string section, string key, string value)
    {
        var path = LocalConfigPath;

        JsonObject root;
        if (File.Exists(path))
        {
            try
            {
                var existing = File.ReadAllText(path);
                root = JsonNode.Parse(existing)?.AsObject() ?? new JsonObject();
            }
            catch
            {
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
        File.WriteAllText(path, root.ToJsonString(options));
    }
}
