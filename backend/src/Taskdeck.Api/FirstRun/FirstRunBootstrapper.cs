using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
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
    private const string LocalConfigFileName = "appsettings.local.json";

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

    // Match the leniency of the JSON configuration provider (JsonConfigurationFileParser allows comments
    // and trailing commas) so a hand-edited but provider-loadable appsettings.local.json is read the same
    // way here -- and is NOT wrongly treated as corrupt (which would quarantine it) or as missing a key.
    private static readonly System.Text.Json.JsonDocumentOptions LocalConfigJsonOptions = new()
    {
        CommentHandling = System.Text.Json.JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Resolves the one local-config path a host must use for its lifetime. Only a non-headless
    /// Production desktop uses durable per-user storage; development, tests, and headless Production
    /// retain the executable-local compatibility path.
    /// </summary>
    internal static string ResolveLocalConfigPath(bool isProduction, bool isHeadless)
        => ResolveLocalConfigPath(
            isProduction,
            isHeadless,
            AppContext.BaseDirectory,
            isProduction && !isHeadless ? GetAppDataPath() : AppContext.BaseDirectory);

    /// <summary>Path-injected core for deterministic tests.</summary>
    internal static string ResolveLocalConfigPath(
        bool isProduction,
        bool isHeadless,
        string executableDirectory,
        string appDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataDirectory);

        var directory = isProduction && !isHeadless
            ? appDataDirectory
            : executableDirectory;
        return Path.GetFullPath(Path.Combine(directory, LocalConfigFileName));
    }

    /// <summary>
    /// Resolves the optional environment override applied to the MCP stdio Generic Host. The Generic Host's
    /// native <c>DOTNET_ENVIRONMENT</c> wins; <c>ASPNETCORE_ENVIRONMENT</c> is a compatibility fallback.
    /// Returning <see langword="null"/> leaves command-line/default host selection authoritative.
    /// </summary>
    internal static string? ResolveMcpStdioEnvironmentOverride(
        string? dotnetEnvironment,
        string? aspNetCoreEnvironment)
    {
        if (!string.IsNullOrWhiteSpace(dotnetEnvironment))
        {
            return dotnetEnvironment.Trim();
        }

        return string.IsNullOrWhiteSpace(aspNetCoreEnvironment)
            ? null
            : aspNetCoreEnvironment.Trim();
    }

    /// <summary>
    /// Prepares the exact path before a configuration provider reads it. A valid v0.1 executable-local
    /// file is imported whole when the durable target is absent. The source is retained, and an existing
    /// durable target always wins without merge or overwrite.
    /// </summary>
    internal static void PrepareLocalConfigFile(
        string localConfigPath,
        string legacyLocalConfigPath,
        bool requireOwnerOnly = false,
        Action<string>? restrictFile = null)
    {
        ImportLegacyLocalConfigIfNeeded(
            legacyLocalConfigPath,
            localConfigPath,
            restrictFile: restrictFile);

        if (requireOwnerOnly && FileExistsOrThrow(localConfigPath, "durable local config"))
        {
            var restrict = restrictFile ?? RestrictFileToCurrentUser;
            RestrictForMigration(restrict, localConfigPath, "durable local config");
            EnsureCompleteJsonObject(
                ReadMigrationFile(localConfigPath, "durable local config"),
                localConfigPath);
        }

        QuarantineCorruptLocalConfigAt(localConfigPath);
        RestrictExistingLocalConfigFileAt(
            localConfigPath,
            requireSuccess: requireOwnerOnly,
            restrictFile: restrictFile);
    }

    /// <summary>
    /// Atomically imports a complete legacy JSON object into an absent durable path. Both files are
    /// restricted to the current user, the source is retained, and any ambiguity fails closed.
    /// </summary>
    internal static void ImportLegacyLocalConfigIfNeeded(
        string legacyPath,
        string durablePath,
        Action<string>? beforeRead = null,
        Action<string>? restrictFile = null)
    {
        var normalizedLegacyPath = Path.GetFullPath(legacyPath);
        var normalizedDurablePath = Path.GetFullPath(durablePath);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.Equals(normalizedLegacyPath, normalizedDurablePath, comparison))
        {
            return;
        }

        WithBootstrapLock(normalizedLegacyPath, TimeSpan.FromSeconds(10), () =>
            WithBootstrapLock(normalizedDurablePath, TimeSpan.FromSeconds(10), () =>
            {
                var restrict = restrictFile ?? RestrictFileToCurrentUser;
                if (FileExistsOrThrow(normalizedDurablePath, "durable local config"))
                {
                    RestrictForMigration(restrict, normalizedDurablePath, "durable local config");
                    if (FileExistsOrThrow(normalizedLegacyPath, "legacy local config"))
                    {
                        RestrictForMigration(restrict, normalizedLegacyPath, "retained legacy local config");
                    }

                    return true;
                }

                if (!FileExistsOrThrow(normalizedLegacyPath, "legacy local config"))
                {
                    return true;
                }

                if (HasCorruptRecoveryEvidence(normalizedDurablePath))
                {
                    throw new InvalidOperationException(
                        $"First-run: Recovery evidence for a prior durable local config exists beside " +
                        $"{normalizedDurablePath}. Refusing to import an older legacy config until the " +
                        "recovery evidence is resolved.");
                }

                RestrictForMigration(restrict, normalizedLegacyPath, "legacy local config");

                byte[] payload;
                try
                {
                    using var source = new FileStream(
                        normalizedLegacyPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read | FileShare.Delete);
                    beforeRead?.Invoke(normalizedLegacyPath);
                    using var buffer = new MemoryStream();
                    source.CopyTo(buffer);
                    payload = buffer.ToArray();
                    EnsureCompleteJsonObject(payload, normalizedLegacyPath);
                }
                catch (InvalidOperationException)
                {
                    throw;
                }
                catch (Exception ex) when (
                    ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    throw new InvalidOperationException(
                        $"First-run: Could not securely read the legacy local config at " +
                        $"{normalizedLegacyPath}; refusing to generate replacement secrets.", ex);
                }

                RestrictForMigration(restrict, normalizedLegacyPath, "current legacy local config");
                var currentPayload = ReadMigrationFile(normalizedLegacyPath, "legacy local config");
                if (!payload.AsSpan().SequenceEqual(currentPayload))
                {
                    throw new InvalidOperationException(
                        $"First-run: The legacy local config at {normalizedLegacyPath} changed while it " +
                        "was being read; refusing to import stale or unverified bytes.");
                }

                var directory = Path.GetDirectoryName(normalizedDurablePath)
                    ?? throw new InvalidOperationException(
                        $"First-run: Could not resolve the durable config directory for " +
                        $"{normalizedDurablePath}.");
                try
                {
                    Directory.CreateDirectory(directory);
                }
                catch (Exception ex) when (
                    ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    throw new InvalidOperationException(
                        $"First-run: Could not create durable config directory {directory}; refusing to " +
                        "generate replacement secrets.", ex);
                }

                var tempPath = Path.Combine(
                    directory,
                    $".{Path.GetFileName(normalizedDurablePath)}.{Guid.NewGuid():N}.import.tmp");
                try
                {
                    WriteRestrictedFile(tempPath, payload);
                    try
                    {
                        File.Move(tempPath, normalizedDurablePath);
                    }
                    catch (IOException ex)
                    {
                        if (!FileExistsOrThrow(normalizedDurablePath, "concurrent durable local config")
                            || !IsCompleteJsonObjectFile(normalizedDurablePath))
                        {
                            throw new InvalidOperationException(
                                $"First-run: Could not atomically import the legacy local config into " +
                                $"{normalizedDurablePath}; refusing to generate replacement secrets.", ex);
                        }
                    }

                    RestrictForMigration(restrict, normalizedDurablePath, "durable local config");
                }
                finally
                {
                    try { File.Delete(tempPath); } catch { /* restricted temp; best-effort cleanup */ }
                }

                RestrictForMigration(restrict, normalizedLegacyPath, "retained legacy local config");
                RestrictForMigration(restrict, normalizedDurablePath, "durable local config");
                return true;
            }));
    }

    private static void RestrictForMigration(Action<string> restrict, string path, string description)
    {
        try
        {
            restrict(path);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new InvalidOperationException(
                $"First-run: Could not restrict the {description} at {path} to the current user; refusing " +
                "to read, copy, or generate secrets.", ex);
        }
    }

    private static byte[] ReadMigrationFile(string path, string description)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new InvalidOperationException(
                $"First-run: Could not read the {description} at {path}; refusing migration.", ex);
        }
    }

    private static bool HasCorruptRecoveryEvidence(string durablePath)
    {
        var directory = Path.GetDirectoryName(durablePath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return false;
        }

        try
        {
            return Directory.EnumerateFiles(
                    directory,
                    $"{Path.GetFileName(durablePath)}.corrupt-*",
                    SearchOption.TopDirectoryOnly)
                .Any();
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new InvalidOperationException(
                $"First-run: Could not inspect recovery evidence beside {durablePath}; refusing migration.",
                ex);
        }
    }

    private static bool FileExistsOrThrow(string path, string description)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                throw new InvalidOperationException(
                    $"First-run: The {description} path {path} is a directory; refusing to generate " +
                    "replacement secrets.");
            }

            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
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
                $"First-run: Could not inspect the {description} path {path}; refusing to generate " +
                "replacement secrets.", ex);
        }
    }

    private static bool IsCompleteJsonObjectFile(string path)
    {
        try
        {
            EnsureCompleteJsonObject(File.ReadAllBytes(path), path);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new InvalidOperationException(
                $"First-run: Could not validate the durable local config at {path}; refusing startup.", ex);
        }
    }

    private static void EnsureCompleteJsonObject(byte[] payload, string path)
    {
        try
        {
            _ = JsonNode.Parse(payload, nodeOptions: null, documentOptions: LocalConfigJsonOptions)?.AsObject()
                ?? throw new JsonException("The JSON root is null.");
            using var stream = new MemoryStream(payload, writable: false);
            _ = new ConfigurationBuilder().AddJsonStream(stream).Build();
        }
        catch (Exception ex) when (
            ex is JsonException or InvalidOperationException or FormatException or InvalidDataException)
        {
            throw new InvalidOperationException(
                $"First-run: The local config at {path} is not a complete provider-loadable JSON object; " +
                "refusing to generate replacement secrets.", ex);
        }
    }

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
        => AddLocalConfigFile(builder, LegacyLocalConfigPath);

    public static WebApplicationBuilder AddLocalConfigFile(
        this WebApplicationBuilder builder,
        string localConfigPath)
        => AddLocalConfigFile(
            builder,
            localConfigPath,
            IsHeadlessEnvironment());

    public static WebApplicationBuilder AddLocalConfigFile(
        this WebApplicationBuilder builder,
        string localConfigPath,
        bool isBootstrapHeadless)
    {
        // Prepare before the provider reads: import a valid legacy file into an absent durable path, enforce
        // owner-only permissions, and validate the durable Production file without replacing corrupt
        // evidence. Compatibility hosts retain the existing secure quarantine behavior for malformed files.
        var exactPath = Path.GetFullPath(localConfigPath);
        PrepareLocalConfigFile(
            exactPath,
            LegacyLocalConfigPath,
            requireOwnerOnly: builder.Environment.IsProduction() && !isBootstrapHeadless);

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
            Path = exactPath,
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
    /// config source loads as missing instead of throwing at config-build time. Recovery-copy creation is
    /// mandatory; a file that cannot be removed after preservation is reported and remains fail-closed at
    /// configuration load.
    /// </summary>
    internal static void QuarantineCorruptLocalConfig() => QuarantineCorruptLocalConfigAt(LocalConfigPath);

    /// <summary>Path-parameterized core of <see cref="QuarantineCorruptLocalConfig"/> (testable seam).</summary>
    internal static void QuarantineCorruptLocalConfigAt(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        // Separate the READ from the PARSE: a present-but-temporarily-unreadable file (transient I/O / share
        // violation / permission glitch) may be perfectly valid and hold the only connector key, so it must
        // NOT be quarantined/deleted -- only a genuine JSON parse failure means the file would crash config
        // build and should be quarantined.
        string content;
        try
        {
            content = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[FirstRun] WARNING: could not read {path} ({ex.Message}); leaving it in place (not " +
                "quarantining -- it may be a valid file holding the connector key).");
            return;
        }

        try
        {
            // JsonConfigurationProvider requires a JSON OBJECT at the root; AsObject() throws otherwise
            // (and Parse throws on empty/truncated/invalid content), matching what would crash config build.
            // Use the provider's leniency (comments / trailing commas) so a loadable file is not quarantined.
            _ = JsonNode.Parse(content, nodeOptions: null, documentOptions: LocalConfigJsonOptions)?.AsObject();
            return; // Parses as a JSON object -> usable, leave it alone.
        }
        catch (Exception ex)
        {
            // Genuine parse failure (malformed JSON / non-object root). Preserve for recovery, then remove
            // so the optional config source can load as missing and the later database/key guard can decide
            // whether a fresh identity is safe.
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
    /// <see cref="AddLocalConfigFile(WebApplicationBuilder, string)"/> and after the standard
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
        => RunFirstRunChecks(builder, logger, LegacyLocalConfigPath);

    public static WebApplicationBuilder RunFirstRunChecks(
        this WebApplicationBuilder builder,
        ILogger logger,
        string localConfigPath)
        => RunFirstRunChecks(
            builder,
            logger,
            localConfigPath,
            IsHeadlessEnvironment());

    public static WebApplicationBuilder RunFirstRunChecks(
        this WebApplicationBuilder builder,
        ILogger logger,
        string localConfigPath,
        bool isBootstrapHeadless)
    {
        var exactPath = Path.GetFullPath(localConfigPath);
        var isProduction = builder.Environment.IsProduction();
        var resolveDatabaseToAppData = !builder.Environment.IsDevelopment() && !isBootstrapHeadless;
        var databaseAppDataPath = resolveDatabaseToAppData
            ? isProduction
                ? Path.GetDirectoryName(exactPath)
                : GetAppDataPath()
            : null;

        // Recover/check connector identity before either secret is generated. An existing database with no
        // recoverable connector key must not be paired with a new key that silently orphans credentials.
        EnsureBootstrapSecrets(
            builder.Configuration,
            logger,
            exactPath,
            isProduction,
            isBootstrapHeadless,
            resolveDatabaseToAppData,
            databaseAppDataPath);

        if (!resolveDatabaseToAppData)
        {
            return builder;
        }

        EnsureDbPath(builder.Configuration, logger, exactPath, databaseAppDataPath!);
        return builder;
    }

    /// <summary>
    /// Validates that no placeholder JWT secret is being used in Production.
    /// Unlike <see cref="RunFirstRunChecks(WebApplicationBuilder, ILogger, string)"/> (which auto-generates secrets for
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
        => ValidateProductionSecrets(builder, logger, LegacyLocalConfigPath);

    public static WebApplicationBuilder ValidateProductionSecrets(
        this WebApplicationBuilder builder,
        ILogger logger,
        string localConfigPath)
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
                $"key may already be in {Path.GetFullPath(localConfigPath)} -- reuse that value rather than generating a new " +
                "one, or stored connector credentials will become unrecoverable.)");
        }

        logger.LogInformation("Production secret validation passed.");
        return builder;
    }

    // -------------------------------------------------------------------------

    internal static string LegacyLocalConfigPath
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, LocalConfigFileName));

    // Backward-compatible test alias. Runtime hosts resolve once and thread the exact path explicitly.
    internal static string LocalConfigPath => LegacyLocalConfigPath;

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
            // Read through a throwaway configuration builder so the lookup matches the REAL provider exactly:
            // case-insensitive section/key names AND the comment/trailing-comma leniency. A direct JsonNode
            // walk would miss a provider-valid case variant (e.g. "connectors": { "encryptionkey": ... }).
            var config = new ConfigurationBuilder()
                .AddJsonFile(path, optional: true, reloadOnChange: false)
                .Build();
            key = config["Connectors:EncryptionKey"];
            return !string.IsNullOrWhiteSpace(key);
        }
        catch
        {
            // Unparsable / unreadable: treat as "no recoverable key persisted".
            key = null;
            return false;
        }
    }

    /// <summary>
    /// Recovers persisted connector identity and refuses replacement-secret generation when an existing
    /// database has no supplied or recoverable key. Connector identity is established before JWT identity.
    /// </summary>
    internal static void EnsureBootstrapSecrets(
        IConfiguration configuration,
        ILogger logger,
        string localConfigPath,
        bool isProduction,
        bool isHeadless,
        bool resolveDatabaseToAppData,
        string? databaseAppDataPath = null,
        string? legacyDatabaseDirectory = null)
    {
        string? recoveredPersistedKey = null;
        if (string.IsNullOrWhiteSpace(configuration["Connectors:EncryptionKey"])
            && TryReadPersistedConnectorKey(localConfigPath, out recoveredPersistedKey))
        {
            configuration["Connectors:EncryptionKey"] = recoveredPersistedKey;
            logger.LogWarning(
                "First-run: A higher-priority configuration source is masking the connector key persisted " +
                "in {ConfigFile}. Reusing the persisted key so stored connector credentials stay " +
                "decryptable; unset an empty Connectors__EncryptionKey value to silence this warning.",
                localConfigPath);
        }

        var configuredDataSource = ExtractDataSource(
            configuration.GetConnectionString("DefaultConnection") ?? string.Empty);
        var hasExplicitAbsoluteDatabasePath = !string.IsNullOrWhiteSpace(configuredDataSource)
            && Path.IsPathRooted(configuredDataSource);
        var databaseTargetIsPersistedLocally = hasExplicitAbsoluteDatabasePath
            && PersistedDatabaseTargetMatches(localConfigPath, configuredDataSource);
        var databasePath = ResolveDatabaseFilePath(
            configuration,
            resolveDatabaseToAppData,
            databaseAppDataPath);
        var databaseExists = databasePath is not null
            && FileExistsOrThrow(databasePath, "resolved SQLite database");
        if (databasePath is not null
            && !databaseExists
            && HasSqliteSidecarEvidence(databasePath, "resolved SQLite database"))
        {
            throw new InvalidOperationException(
                $"First-run: The resolved SQLite database {databasePath} is absent, but WAL or shared-memory " +
                "recovery evidence exists beside it. Refusing to generate replacement identity or create a " +
                "new database over potentially recoverable state. Stop Taskdeck and recover the database " +
                "together with its -wal and -shm sidecars before retrying.");
        }

        if (databasePath is not null
            && resolveDatabaseToAppData
            && (!hasExplicitAbsoluteDatabasePath || databaseTargetIsPersistedLocally)
            && !databaseExists)
        {
            var legacyDatabasePath = Path.GetFullPath(Path.Combine(
                legacyDatabaseDirectory ?? AppContext.BaseDirectory,
                Path.GetFileName(databasePath)));
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!string.Equals(databasePath, legacyDatabasePath, comparison)
                && (FileExistsOrThrow(legacyDatabasePath, "legacy executable-local SQLite database")
                    || HasSqliteSidecarEvidence(
                        legacyDatabasePath,
                        "legacy executable-local SQLite database")))
            {
                throw new InvalidOperationException(
                    $"First-run: The configured per-user database target {databasePath} is absent, but a " +
                    $"legacy executable-local SQLite file or sidecar exists at {legacyDatabasePath}. " +
                    "Refusing to generate " +
                    "new identity or create a blank database that would silently abandon v0.1 data. Stop " +
                    "Taskdeck and recover the SQLite database together with any -wal and -shm sidecars " +
                    "before retrying.");
            }
        }

        if (databaseExists
            && string.IsNullOrWhiteSpace(configuration["Connectors:EncryptionKey"]))
        {
            throw new InvalidOperationException(
                $"First-run: An existing database was found at {databasePath}, but no supplied or " +
                $"persisted connector encryption key is recoverable from {localConfigPath}. Refusing to " +
                "generate replacement connector or JWT secrets because stored connector credentials may " +
                "depend on the missing key. Restore the original key or explicitly supply " +
                "Connectors__EncryptionKey before starting Taskdeck.");
        }

        if (ShouldAutoGenerateConnectorKey(isProduction, isHeadless))
        {
            EnsureConnectorEncryptionKey(
                configuration,
                logger,
                localConfigPath,
                requirePersistence: isProduction);
        }

        EnsureJwtSecret(
            configuration,
            logger,
            localConfigPath,
            requirePersistence: isProduction && !isHeadless);

        // Reloading the JSON provider can re-expose an empty higher-priority value. Reapply only the exact
        // recovered key; never generate a different one.
        if (!string.IsNullOrWhiteSpace(recoveredPersistedKey)
            && string.IsNullOrWhiteSpace(configuration["Connectors:EncryptionKey"]))
        {
            configuration["Connectors:EncryptionKey"] = recoveredPersistedKey;
        }
    }

    private static string? ResolveDatabaseFilePath(
        IConfiguration configuration,
        bool resolveDatabaseToAppData,
        string? databaseAppDataPath)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
        var dataSource = ExtractDataSource(connectionString);
        if (string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var dbFile = string.IsNullOrWhiteSpace(dataSource) ? "taskdeck.db" : dataSource;
        if (Path.IsPathRooted(dbFile))
        {
            return Path.GetFullPath(dbFile);
        }

        var shouldResolveAppData = resolveDatabaseToAppData
            && (configuration.GetValue<bool?>("FirstRun:ResolveAppDataDbPath") ?? true);
        return shouldResolveAppData
            ? Path.GetFullPath(Path.Combine(
                databaseAppDataPath ?? GetAppDataPath(),
                Path.GetFileName(dbFile)))
            : Path.GetFullPath(dbFile);
    }

    private static bool HasSqliteSidecarEvidence(string databasePath, string description)
        => FileExistsOrThrow($"{databasePath}-wal", $"{description} WAL sidecar")
            || FileExistsOrThrow($"{databasePath}-shm", $"{description} shared-memory sidecar");

    private static bool PersistedDatabaseTargetMatches(string localConfigPath, string effectiveDataSource)
    {
        if (!File.Exists(localConfigPath))
        {
            return false;
        }

        try
        {
            var persisted = new ConfigurationBuilder()
                .AddJsonFile(localConfigPath, optional: false, reloadOnChange: false)
                .Build()
                .GetConnectionString("DefaultConnection");
            var persistedDataSource = ExtractDataSource(persisted ?? string.Empty);
            if (!Path.IsPathRooted(persistedDataSource))
            {
                return false;
            }

            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return string.Equals(
                Path.GetFullPath(persistedDataSource),
                Path.GetFullPath(effectiveDataSource),
                comparison);
        }
        catch
        {
            // The runtime preparation path already fails closed on an unreadable or corrupt durable file.
            // Treat a direct unit-call failure here as not proven to be the migrated legacy target.
            return false;
        }
    }

    internal static void EnsureJwtSecret(
        IConfiguration configuration,
        ILogger logger,
        string localConfigPath,
        bool requirePersistence = false,
        Action? afterInitialAbsence = null,
        Func<string>? secretFactory = null)
    {
        var configured = configuration["Jwt:SecretKey"] ?? string.Empty;

        if (!IsPlaceholder(configured))
        {
            return;
        }

        afterInitialAbsence?.Invoke();
        PersistedValueResult result;
        try
        {
            result = PersistGeneratedValue(
                localConfigPath,
                "Jwt",
                "SecretKey",
                secretFactory ?? GenerateSecret,
                value => !IsPlaceholder(value));
            // Reload so subsequent configuration reads get the new value.
            if (configuration is IConfigurationRoot root)
            {
                root.Reload();
            }

            configuration["Jwt:SecretKey"] = result.Value;
            logger.LogInformation(
                result.Created
                    ? "First-run: JWT secret was not configured. A random secret has been generated and " +
                        "saved to {ConfigFile}."
                    : "First-run: Another startup persisted the JWT secret in {ConfigFile}. Reusing that " +
                        "winning value.",
                localConfigPath);
        }
        catch (IOException ex)
        {
            if (requirePersistence)
            {
                throw new InvalidOperationException(
                    $"First-run: Could not persist the JWT secret to {localConfigPath} ({ex.Message}). " +
                    "Desktop Production refuses a transient secret that would invalidate sessions on " +
                    "restart.", ex);
            }

            configuration["Jwt:SecretKey"] = (secretFactory ?? GenerateSecret)();
            logger.LogWarning(
                "First-run: Could not persist JWT secret to {ConfigFile} ({Error}). " +
                "A transient in-memory secret has been generated instead.",
                localConfigPath, ex.Message);
        }
    }

    internal static void EnsureConnectorEncryptionKey(
        IConfiguration configuration,
        ILogger logger,
        string localConfigPath,
        bool requirePersistence = false,
        Action? afterInitialAbsence = null,
        Func<string>? secretFactory = null)
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
        if (TryReadPersistedConnectorKey(localConfigPath, out var persisted))
        {
            configuration["Connectors:EncryptionKey"] = persisted;
            logger.LogWarning(
                "First-run: A higher-priority configuration source (likely an empty Connectors__EncryptionKey " +
                "environment variable) is masking the connector key persisted in {ConfigFile}. Reusing the " +
                "persisted key so stored connector credentials stay decryptable; unset the empty variable to " +
                "silence this warning.", localConfigPath);
            return;
        }

        afterInitialAbsence?.Invoke();
        PersistedValueResult result;
        try
        {
            result = PersistGeneratedValue(
                localConfigPath,
                "Connectors",
                "EncryptionKey",
                secretFactory ?? GenerateSecret,
                value => !string.IsNullOrWhiteSpace(value));
            if (configuration is IConfigurationRoot root)
            {
                root.Reload();
            }

            logger.LogInformation(
                result.Created
                    ? "First-run: Connector encryption key was not configured. A random key has been " +
                        "generated and saved to {ConfigFile}. BACK UP THIS FILE alongside your database -- " +
                        "it is required to decrypt stored connector credentials."
                    : "First-run: Another startup persisted the connector encryption key in {ConfigFile}. " +
                        "Reusing that winning key.",
                localConfigPath);
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
                    $"First-run: Could not persist the connector encryption key to {localConfigPath} " +
                    $"({ex.Message}). A run-once in-memory key would be lost on restart and make stored " +
                    "connector credentials unrecoverable. Ensure the local-config directory is writable AND on " +
                    "a filesystem that supports owner-only file permissions (NTFS / a POSIX filesystem; " +
                    "FAT32/exFAT or some network shares cannot lock the secret down), or set a stable key via " +
                    "the Connectors__EncryptionKey environment variable.", ex);
            }

            // Non-Production (dev/staging/test): a transient in-memory key is acceptable -- these do not
            // carry credentials that must survive a restart, and parallel test harnesses can lock the file.
            configuration["Connectors:EncryptionKey"] = (secretFactory ?? GenerateSecret)();
            logger.LogWarning(
                "First-run: Could not persist connector encryption key to {ConfigFile} ({Error}). " +
                "A transient in-memory key has been generated instead.",
                localConfigPath, ex.Message);
            return;
        }

        // The key is now persisted on disk. Make sure it is also the effective in-process value: a reload may
        // not propagate through every provider (test harnesses), and an empty higher-priority source could
        // mask the freshly written file. Either way the key IS persisted and recoverable -- the next launch
        // reads it back (and reuses it via TryReadPersistedConnectorKey if still masked) -- so an in-memory
        // value here is safe (no data loss), unlike overwriting an existing key would have been.
        if (!string.Equals(
                configuration["Connectors:EncryptionKey"],
                result.Value,
                StringComparison.Ordinal))
        {
            configuration["Connectors:EncryptionKey"] = result.Value;
            if (requirePersistence)
            {
                // In Production the only way the just-persisted key is not the effective value is a
                // higher-priority empty source masking it. The key is safe on disk and will be reused on the
                // next launch; surface the misconfiguration so the operator can clear it.
                logger.LogWarning(
                    "First-run: The connector key was persisted to {ConfigFile} but a higher-priority " +
                    "configuration source (likely an empty Connectors__EncryptionKey environment variable) is " +
                    "masking it. The persisted key will be reused on the next launch; unset the empty variable.",
                    localConfigPath);
            }
        }
    }

    internal static void EnsureDbPath(
        IConfiguration configuration,
        ILogger logger,
        string localConfigPath,
        string appDataPath)
    {
        var resolveAppData = configuration.GetValue<bool?>("FirstRun:ResolveAppDataDbPath") ?? true;
        if (!resolveAppData)
        {
            return;
        }

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? string.Empty;
        var dataSource = ExtractDataSource(connectionString);

        if (string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Already an absolute path — nothing to do.
        if (!string.IsNullOrWhiteSpace(dataSource) && Path.IsPathRooted(dataSource))
        {
            return;
        }

        Directory.CreateDirectory(appDataPath);

        var dbFile = string.IsNullOrWhiteSpace(dataSource)
            ? "taskdeck.db"
            : Path.GetFileName(dataSource);

        var resolvedPath = Path.GetFullPath(Path.Combine(appDataPath, dbFile));
        var resolvedConnectionString = $"Data Source={resolvedPath}";

        // Write into the local config file so the value is picked up by
        // AddInfrastructure later in the startup pipeline.
        try
        {
            PersistValue(localConfigPath, "ConnectionStrings", "DefaultConnection", resolvedConnectionString);
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
                localConfigPath, ex.Message);
            return;
        }

        if (configuration is IConfigurationRoot root)
        {
            root.Reload();
        }

        // A command-line or environment provider can keep exposing the original relative value after reload.
        // Install the exact resolved value so AddInfrastructure opens the file the safety check inspected.
        configuration["ConnectionStrings:DefaultConnection"] = resolvedConnectionString;

        logger.LogInformation(
            "First-run: SQLite DB path resolved to AppData location: {DbPath}", resolvedPath);
    }

    internal static bool IsHeadlessEnvironment()
        => DesktopRuntime.IsBrowserSuppressedEnvironment();

    internal static string GetAppDataPath()
    {
        // Known Folder lookup ignores a test/portable-launcher LOCALAPPDATA override on Windows. Honour the
        // standard absolute variable first so packaged smoke can isolate a profile without touching real data.
        var localAppDataOverride = OperatingSystem.IsWindows()
            ? Environment.GetEnvironmentVariable("LOCALAPPDATA")
            : null;
        var knownFolderPath = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create);
        return ResolveAppDataPath(localAppDataOverride, knownFolderPath);
    }

    internal static string ResolveAppDataPath(string? localAppDataOverride, string knownFolderPath)
    {
        var localAppData = string.IsNullOrWhiteSpace(localAppDataOverride)
            ? knownFolderPath
            : localAppDataOverride;
        if (string.IsNullOrWhiteSpace(localAppData) || !Path.IsPathRooted(localAppData))
        {
            throw new InvalidOperationException(
                "First-run: Could not resolve an absolute per-user LocalApplicationData directory.");
        }

        return Path.GetFullPath(Path.Combine(localAppData, "Taskdeck"));
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
    /// by an operator instead of being silently overwritten. Preservation is mandatory: if a restricted
    /// recovery copy cannot be created, the original remains and bootstrap fails closed.
    /// </summary>
    private static void PreserveCorruptConfig(string path, Exception parseError)
    {
        var backupPath = $"{path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
        try
        {
            // The .corrupt-* backup holds the same secrets as the original (the connector key in
            // particular), so create it ATOMICALLY with owner-only permissions (#1241/#1264) -- a
            // copy-then-restrict sequence would briefly expose it via the directory's default ACL
            // (File.Copy does not carry the source's security descriptor). Byte-faithful read: the file is
            // unparsable, possibly from an interrupted write, so no text decoding is applied.
            WriteRestrictedFile(backupPath, File.ReadAllBytes(path));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"First-run: {path} contains invalid JSON ({parseError.Message}) and could not be " +
                $"preserved securely at {backupPath}. Leaving the original untouched and refusing to " +
                "generate replacement secrets.", ex);
        }

        Console.Error.WriteLine(
            $"[FirstRun] WARNING: {path} contains invalid JSON ({parseError.Message}). A copy was " +
            $"preserved at {backupPath} for recovery -- it may hold a previously-generated key -- and the " +
            "file will be rewritten.");
    }

    private readonly record struct PersistedValueResult(string Value, bool Created);

    /// <summary>Atomically reuses an acceptable persisted value or creates exactly one winner.</summary>
    private static PersistedValueResult PersistGeneratedValue(
        string path,
        string section,
        string key,
        Func<string> valueFactory,
        Func<string, bool> acceptExisting)
    {
        path = Path.GetFullPath(path);
        return WithBootstrapLock(path, TimeSpan.FromSeconds(10), () =>
        {
            JsonObject root;
            if (File.Exists(path))
            {
                try
                {
                    var payload = File.ReadAllBytes(path);
                    EnsureCompleteJsonObject(payload, path);
                    root = JsonNode.Parse(
                        payload,
                        nodeOptions: null,
                        documentOptions: LocalConfigJsonOptions)?.AsObject() ?? new JsonObject();

                    using var stream = new MemoryStream(payload, writable: false);
                    var current = new ConfigurationBuilder().AddJsonStream(stream).Build()[$"{section}:{key}"];
                    if (current is not null && acceptExisting(current))
                    {
                        return new PersistedValueResult(current, Created: false);
                    }
                }
                catch (Exception ex)
                {
                    PreserveCorruptConfig(path, ex);
                    root = new JsonObject();
                }
            }
            else
            {
                root = new JsonObject();
            }

            var value = valueFactory();
            SetJsonValue(root, section, key, value);
            WriteLocalConfigRoot(path, root);
            return new PersistedValueResult(value, Created: true);
        });
    }

    private static void SetJsonValue(JsonObject root, string section, string key, string value)
    {
        var flattenedName = $"{section}:{key}";
        foreach (var property in root.ToList())
        {
            if (string.Equals(property.Key, flattenedName, StringComparison.OrdinalIgnoreCase))
            {
                root[property.Key] = value;
                return;
            }
        }

        var sectionProperty = root.FirstOrDefault(
            property => string.Equals(property.Key, section, StringComparison.OrdinalIgnoreCase));
        var sectionNode = sectionProperty.Value as JsonObject;
        if (sectionNode is null)
        {
            sectionNode = new JsonObject();
            root[string.IsNullOrEmpty(sectionProperty.Key) ? section : sectionProperty.Key] = sectionNode;
        }

        var keyProperty = sectionNode.FirstOrDefault(
            property => string.Equals(property.Key, key, StringComparison.OrdinalIgnoreCase));
        sectionNode[string.IsNullOrEmpty(keyProperty.Key) ? key : keyProperty.Key] = value;
    }

    private static void WriteLocalConfigRoot(string path, JsonObject root)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            WriteRestrictedFile(
                tempPath,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            ReplaceFileWithRetry(tempPath, path);
        }
        catch
        {
            try { File.Delete(tempPath); } catch { /* best-effort restricted-temp cleanup */ }
            throw;
        }
    }

    private static void PersistValue(string path, string section, string key, string value)
        => PersistGeneratedValue(path, section, key, () => value, _ => false);

    /// <summary>
    /// Best-effort re-restriction of an existing persisted local config to the current user (#1241 forward
    /// remediation). Heals installs upgraded from a build that wrote the file world-readable. Never throws:
    /// at this pre-DI stage a failure is reported to stderr and startup continues.
    /// </summary>
    internal static void RestrictExistingLocalConfigFile()
        => RestrictExistingLocalConfigFileAt(LegacyLocalConfigPath);

    internal static void RestrictExistingLocalConfigFileAt(
        string path,
        bool requireSuccess = false,
        Action<string>? restrictFile = null)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            (restrictFile ?? RestrictFileToCurrentUser)(path);
        }
        catch (Exception ex)
        {
            if (requireSuccess)
            {
                throw new InvalidOperationException(
                    $"First-run: Could not restrict {path} to the current user; refusing startup because " +
                    "the file may contain connector and JWT secrets.", ex);
            }

            Console.Error.WriteLine(
                $"[FirstRun] WARNING: could not re-restrict permissions on the existing {path} " +
                $"({ex.Message}); it may remain readable by other local users until permissions are fixed.");
        }
    }

    /// <summary>
    /// Creates <paramref name="path"/> ATOMICALLY with owner-only permissions and
    /// <see cref="FileShare.None"/>, then writes <paramref name="contents"/> through that same handle
    /// (#1264). On Unix the file is born <c>0600</c> (<see cref="FileStreamOptions.UnixCreateMode"/>) and
    /// the exact mode is then pinned through the open handle (umask-proof); on Windows the protected
    /// owner-only DACL is supplied to <c>CreateFile</c> itself and read back through the open handle, so a
    /// filesystem that silently ignores security descriptors (FAT32/exFAT, some SMB shares) fails closed
    /// instead of persisting the secret unprotected. Unlike create-then-restrict there is no instant at
    /// which another local user can open the file, and no pre-opened handle can survive into the written
    /// secret; <see cref="FileMode.CreateNew"/> additionally refuses to adopt a file someone pre-created at
    /// the path. Any failure is normalized to
    /// <see cref="IOException"/> (matching <see cref="RestrictFileToCurrentUser"/>) so first-run callers'
    /// <c>catch (IOException)</c> falls back to an in-memory value; a partially-written file is
    /// best-effort deleted so callers never observe a half-written secret file.
    /// </summary>
    internal static void WriteRestrictedFile(string path, string contents)
        => WriteRestrictedFile(path, Encoding.UTF8.GetBytes(contents));

    internal static void WriteRestrictedFile(string path, byte[] contents)
    {
        FileStream stream;
        try
        {
            stream = CreateRestrictedNewFile(path);
        }
        catch (IOException)
        {
            // Creation failed -> nothing of ours remains at the path (CreateRestrictedNewFile removes its
            // own file when the post-create lockdown pin/verification fails); never delete what we did not
            // create (CreateNew fails precisely when the path is already occupied).
            throw;
        }
        catch (Exception ex)
        {
            // Normalize (e.g. UnauthorizedAccessException, PlatformNotSupportedException) so the callers'
            // catch(IOException) handles it uniformly -- the secret is never written to an unprotected file.
            throw new IOException(
                $"Could not create {path} restricted to the current user; refusing to write the secret to it.", ex);
        }

        try
        {
            using (stream)
            {
                stream.Write(contents, 0, contents.Length);
            }
        }
        catch (Exception ex)
        {
            // We created the file, so a failed/partial write cleans it up (it was restricted from birth, so
            // this is consistency hygiene, not exposure). Best-effort; the original failure propagates.
            try { File.Delete(path); } catch { /* ignore */ }
            if (ex is IOException)
            {
                throw;
            }

            throw new IOException(
                $"Could not write the restricted file {path}; refusing to leave a partial secret file.", ex);
        }
    }

    private static FileStream CreateRestrictedNewFile(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return CreateOwnerOnlyFileWindows(path);
        }

        var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
        });
        try
        {
            // open(2)'s mode argument is umask-masked (the mask can only STRIP bits from 0600, never widen
            // it) and is silently ignored on non-POSIX filesystems (e.g. vfat). Pin exactly 0600 through the
            // open handle (fchmod -- race-free): this restores the exact-mode guarantee regardless of umask,
            // and it FAILS on filesystems that cannot store the mode exactly where the pre-#1264
            // SetUnixFileMode(path) call failed -- keeping the fail-closed contract instead of silently
            // persisting the secret with whatever mode the mount dictates.
            File.SetUnixFileMode(stream.SafeFileHandle, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return stream;
        }
        catch
        {
            // We created this file; a failed lockdown must not leave it behind. Best-effort; the original
            // exception propagates (normalized to IOException by the caller).
            stream.Dispose();
            try { File.Delete(path); } catch { /* ignore */ }
            throw;
        }
    }

    [SupportedOSPlatform("windows")]
    private static FileStream CreateOwnerOnlyFileWindows(string path)
    {
        // ReadPermissions (READ_CONTROL) is requested alongside Write so the DACL VERIFICATION below can
        // read the security descriptor back through this same exclusive handle.
        var stream = new FileInfo(path).Create(
            FileMode.CreateNew,
            FileSystemRights.Write | FileSystemRights.ReadPermissions,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.None,
            BuildOwnerOnlyFileSecurity());
        try
        {
            // CreateFileW silently IGNORES the supplied security descriptor on filesystems without ACL
            // support (FAT32/exFAT, some SMB shares): the create succeeds and the file is world-readable.
            // The pre-#1264 SetAccessControl path FAILED there (fail-closed). Read the DACL back through the
            // open handle to restore that contract -- on NTFS this merely confirms the atomically-applied
            // descriptor; on a non-ACL volume it throws or reports an unprotected DACL and we refuse.
            var applied = stream.GetAccessControl();
            if (!applied.AreAccessRulesProtected)
            {
                throw new IOException(
                    $"The filesystem hosting {path} did not honor the owner-only ACL (FAT32/exFAT and some " +
                    "network shares cannot store it); refusing to write the secret to an unprotected file.");
            }

            return stream;
        }
        catch
        {
            // We created this file; a failed lockdown verification must not leave it behind. Best-effort;
            // the original exception propagates (normalized to IOException by the caller).
            stream.Dispose();
            try { File.Delete(path); } catch { /* ignore */ }
            throw;
        }
    }

    /// <summary>
    /// Restricts an EXISTING file to the current user only (#1241). On Unix this is <c>0600</c>; on Windows
    /// it replaces the DACL with a single owner-only ACE and disables inheritance (so the directory's
    /// default ACEs -- e.g. BUILTIN\Users read -- do not apply). Used for forward remediation of files that
    /// already exist (<see cref="RestrictExistingLocalConfigFile"/>, the corrupt-backup fallback); NEW
    /// secret files are instead created atomically restricted via
    /// <see cref="WriteRestrictedFile(string, string)"/> (#1264).
    /// Any failure is normalized to <see cref="IOException"/> so callers'
    /// <c>catch (IOException)</c> handle it uniformly.
    /// </summary>
    internal static void RestrictFileToCurrentUser(string path)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                return;
            }

            new FileInfo(path).SetAccessControl(BuildOwnerOnlyFileSecurity());
        }
        catch (IOException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Normalize (e.g. UnauthorizedAccessException, PlatformNotSupportedException) so the callers'
            // catch(IOException) handles it uniformly -- never leave the plaintext secret in an unprotected file.
            throw new IOException(
                $"Could not restrict {path} to the current user; refusing to write the secret to it.", ex);
        }
    }

    [SupportedOSPlatform("windows")]
    private static FileSecurity BuildOwnerOnlyFileSecurity()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var owner = identity.User;
        if (owner is null)
        {
            // Without a resolvable SID we cannot scope the ACL; fail loudly (normalized to IOException by the
            // caller) rather than leave the secret with the inherited, potentially world-readable ACL.
            throw new InvalidOperationException(
                "Could not resolve the current Windows user SID to restrict the secrets file.");
        }

        var security = new FileSecurity();
        // Drop inherited ACEs and grant the current user full control -- the only ACE on the file.
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, AccessControlType.Allow));
        return security;
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

    private static T WithBootstrapLock<T>(string path, TimeSpan timeout, Func<T> action)
    {
        Mutex? mutex = null;
        var acquired = false;
        try
        {
            try
            {
                mutex = new Mutex(initiallyOwned: false, BuildMutexName(path));
                acquired = mutex.WaitOne(timeout);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }
            catch (Exception ex) when (
                ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException or IOException)
            {
                throw new IOException(
                    $"Could not acquire the bootstrap lock for {path}; refusing an unsynchronized write.",
                    ex);
            }

            if (!acquired)
            {
                throw new IOException(
                    $"Timed out waiting for the bootstrap lock for {path}; refusing an unsynchronized write.");
            }

            return action();
        }
        finally
        {
            if (acquired)
            {
                try { mutex?.ReleaseMutex(); } catch (ApplicationException) { }
            }

            mutex?.Dispose();
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
