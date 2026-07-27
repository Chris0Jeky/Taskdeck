using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Taskdeck.Application.Bootstrap;

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
    /// Resolves the one local-config path a host must use for its complete lifetime. Only the desktop
    /// distribution (non-headless Production) uses durable app-data storage; development, staging, test,
    /// and headless Production retain the historical executable-local path.
    /// </summary>
    internal static string ResolveLocalConfigPath(bool isProduction, bool isHeadless)
    {
        var executableDirectory = AppContext.BaseDirectory;
        var appDataDirectory = isProduction && !isHeadless
            ? GetAppDataPath()
            : executableDirectory;
        return ResolveLocalConfigPath(
            isProduction,
            isHeadless,
            executableDirectory,
            appDataDirectory);
    }

    /// <summary>Path-injected core of <see cref="ResolveLocalConfigPath(bool, bool)"/> for tests.</summary>
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
    /// Prepares the resolved config file before any configuration provider reads it. A desktop upgrade
    /// imports the complete executable-local legacy JSON exactly once when the durable target has never
    /// existed. The source is retained; an existing durable file always wins and is never merged or
    /// overwritten.
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
        QuarantineCorruptLocalConfigAt(
            localConfigPath,
            restrictFile: restrictFile,
            requireOwnerOnly: requireOwnerOnly);
        RestrictExistingLocalConfigFileAt(
            localConfigPath,
            requireSuccess: requireOwnerOnly,
            restrictFile: restrictFile);
    }

    /// <summary>
    /// Copies an existing legacy config into an absent durable location through a restricted sibling temp
    /// file and an atomic non-overwriting move. Concurrent importers accept a complete winner-created
    /// target. Source, destination, and permission errors fail closed before configuration or secrets are
    /// generated.
    /// </summary>
    internal static void ImportLegacyLocalConfigIfNeeded(
        string legacyPath,
        string durablePath,
        Action<string>? beforeRead = null,
        Action<string>? restrictFile = null)
    {
        var normalizedLegacyPath = Path.GetFullPath(legacyPath);
        var normalizedDurablePath = Path.GetFullPath(durablePath);
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.Equals(normalizedLegacyPath, normalizedDurablePath, pathComparison))
        {
            return;
        }

        using var legacyLock = BootstrapFileLock.Acquire(normalizedLegacyPath, TimeSpan.FromSeconds(10));
        using var durableLock = BootstrapFileLock.Acquire(normalizedDurablePath, TimeSpan.FromSeconds(10));

        if (FileExistsOrThrow(normalizedDurablePath, "durable local config"))
        {
            if (FileExistsOrThrow(normalizedLegacyPath, "legacy local config"))
            {
                try
                {
                    // The durable file remains authoritative, but the ignored legacy copy can still contain
                    // the same JWT and connector secrets. Keep the retained recovery source owner-only even
                    // when no import is required.
                    (restrictFile ?? RestrictFileToCurrentUser)(normalizedLegacyPath);
                }
                catch (Exception ex) when (
                    ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    throw new InvalidOperationException(
                        $"First-run: Could not restrict the retained legacy local config at " +
                        $"{normalizedLegacyPath} to the current user; refusing to leave duplicate secrets " +
                        "unprotected.", ex);
                }
            }

            return;
        }

        if (!FileExistsOrThrow(normalizedLegacyPath, "legacy local config"))
        {
            return;
        }

        if (HasCorruptRecoveryEvidence(normalizedDurablePath))
        {
            throw new InvalidOperationException(
                $"First-run: Recovery evidence for a prior durable local config exists beside " +
                $"{normalizedDurablePath}. Refusing to import the retained legacy config because it may " +
                "contain an older connector key. Recover or explicitly remove the corrupt backup first.");
        }

        try
        {
            // The retained source still contains every secret being migrated. Lock it down BEFORE opening it,
            // not after copying, so migration never reads secrets from a file other local users can read.
            (restrictFile ?? RestrictFileToCurrentUser)(normalizedLegacyPath);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new InvalidOperationException(
                $"First-run: Could not restrict the legacy local config at {normalizedLegacyPath} to the " +
                "current user; refusing to read or copy its secrets.", ex);
        }

        byte[] payload;
        try
        {
            // Hold a stable handle to the secured source before the test/race seam. A same-user process may
            // atomically replace the path, but this handle continues to identify the exact secured bytes we
            // inspected rather than silently following the replacement.
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
                $"First-run: Could not read the legacy local config at {normalizedLegacyPath}; " +
                "refusing to generate replacement secrets.", ex);
        }

        byte[] currentLegacyPayload;
        try
        {
            // The callback/race seam can atomically replace the path while the stable handle above stays on
            // the original object. Secure whichever object is now current before reading or comparing it.
            (restrictFile ?? RestrictFileToCurrentUser)(normalizedLegacyPath);
            currentLegacyPayload = File.ReadAllBytes(normalizedLegacyPath);
        }
        catch (Exception ex) when (
            ex is FileNotFoundException or DirectoryNotFoundException or IOException
                or UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new InvalidOperationException(
                $"First-run: The legacy local config at {normalizedLegacyPath} changed while it was being " +
                "read; refusing to import stale bytes.", ex);
        }

        if (!payload.AsSpan().SequenceEqual(currentLegacyPayload))
        {
            try
            {
                (restrictFile ?? RestrictFileToCurrentUser)(normalizedLegacyPath);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"First-run: The legacy local config at {normalizedLegacyPath} changed and the current " +
                    "replacement could not be secured owner-only; refusing migration.", ex);
            }

            throw new InvalidOperationException(
                $"First-run: The legacy local config at {normalizedLegacyPath} changed while it was being " +
                "read; refusing to copy either stale or unverified replacement bytes.");
        }

        var durableDirectory = Path.GetDirectoryName(normalizedDurablePath)
            ?? throw new InvalidOperationException(
                $"First-run: Could not resolve the durable local-config directory for {normalizedDurablePath}.");
        try
        {
            Directory.CreateDirectory(durableDirectory);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new InvalidOperationException(
                $"First-run: Could not create the durable local-config directory {durableDirectory}; " +
                "refusing to generate replacement secrets.", ex);
        }

        var tempPath = Path.Combine(
            durableDirectory,
            $".{Path.GetFileName(normalizedDurablePath)}.{Guid.NewGuid():N}.import.tmp");
        try
        {
            WriteRestrictedFile(tempPath, payload);
            try
            {
                // Same-directory, non-overwriting rename: atomic on the supported filesystems and never
                // replaces a durable file another host created after our initial absence check.
                File.Move(tempPath, normalizedDurablePath);
                (restrictFile ?? RestrictFileToCurrentUser)(normalizedDurablePath);
            }
            catch (IOException ex)
            {
                // A concurrent importer may have won. Accept only a complete provider-loadable JSON object;
                // a partial/inaccessible winner is not evidence that migration succeeded.
                if (FileExistsOrThrow(normalizedDurablePath, "durable local config")
                    && IsCompleteJsonObjectFile(normalizedDurablePath))
                {
                    // Do not merely trust that the winner was another Taskdeck process. Pin owner-only
                    // permissions before accepting the target so a raced pre-creation cannot downgrade the
                    // atomic restricted-create guarantee.
                    (restrictFile ?? RestrictFileToCurrentUser)(normalizedDurablePath);
                }
                else
                {
                    throw new InvalidOperationException(
                        $"First-run: Could not atomically import the legacy local config into " +
                        $"{normalizedDurablePath}; refusing to generate replacement secrets.", ex);
                }
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new InvalidOperationException(
                $"First-run: Could not securely import the legacy local config into {normalizedDurablePath}; " +
                "refusing to generate replacement secrets.", ex);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { /* best-effort restricted-temp cleanup */ }
        }

        try
        {
            (restrictFile ?? RestrictFileToCurrentUser)(normalizedLegacyPath);
            var finalLegacyPayload = File.ReadAllBytes(normalizedLegacyPath);
            if (!payload.AsSpan().SequenceEqual(finalLegacyPayload))
            {
                // A non-cooperating writer can replace the path between restriction and read. Re-pin the
                // exact current source before stopping so the retained legacy recovery copy ends owner-only.
                (restrictFile ?? RestrictFileToCurrentUser)(normalizedLegacyPath);
                RemoveImportedDurableIfExact(
                    normalizedDurablePath,
                    payload,
                    restrictFile: restrictFile);

                throw new InvalidOperationException(
                    $"First-run: The legacy local config at {normalizedLegacyPath} changed before migration " +
                    "completed; the stale durable import was removed.");
            }

            (restrictFile ?? RestrictFileToCurrentUser)(normalizedLegacyPath);
            (restrictFile ?? RestrictFileToCurrentUser)(normalizedDurablePath);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"First-run: Could not verify the final legacy and durable local configs as owner-only at " +
                $"{normalizedLegacyPath} and {normalizedDurablePath}; refusing startup.", ex);
        }
    }

    /// <summary>
    /// Atomically captures the current durable path before deciding whether it is the stale import this
    /// startup created. This avoids a compare-then-delete race that could delete a newer replacement.
    /// </summary>
    internal static bool RemoveImportedDurableIfExact(
        string durablePath,
        byte[] expectedPayload,
        Action<string>? afterAtomicCapture = null,
        Action<string>? restrictFile = null)
    {
        var normalizedPath = Path.GetFullPath(durablePath);
        var capturedPath = $"{normalizedPath}.stale-import-{Guid.NewGuid():N}";
        try
        {
            try
            {
                File.Move(normalizedPath, capturedPath, overwrite: false);
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                return false;
            }

            afterAtomicCapture?.Invoke(normalizedPath);
            // Capture first, then secure the exact captured object before inspecting its bytes. If a
            // non-cooperating writer filled the vacated durable path, secure that survivor before any branch
            // returns or throws; a failure retains recovery evidence and stops startup.
            (restrictFile ?? RestrictFileToCurrentUser)(capturedPath);
            var captured = File.ReadAllBytes(capturedPath);
            var currentExists = FileExistsOrThrow(normalizedPath, "replacement durable local config");
            if (currentExists)
            {
                (restrictFile ?? RestrictFileToCurrentUser)(normalizedPath);
            }

            if (expectedPayload.AsSpan().SequenceEqual(captured))
            {
                File.Delete(capturedPath);
                return true;
            }

            if (!currentExists)
            {
                File.Move(capturedPath, normalizedPath, overwrite: false);
            }

            throw new InvalidOperationException(
                $"First-run: The durable local config at {normalizedPath} was replaced while stale migration " +
                "cleanup was deciding what to remove; no unverified replacement was deleted.");
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // If capture succeeded but verification/cleanup failed, restore the exact object whenever the
            // original path is vacant. Otherwise retain both objects for recovery and stop startup.
            if (File.Exists(capturedPath) && !File.Exists(normalizedPath))
            {
                try { File.Move(capturedPath, normalizedPath, overwrite: false); } catch { /* fail closed */ }
            }

            throw new InvalidOperationException(
                $"First-run: Could not safely remove an exact stale durable import at {normalizedPath}; " +
                "no unverified replacement was deleted.", ex);
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
                $"First-run: Could not inspect recovery evidence beside {durablePath}; refusing to import " +
                "the legacy local config.", ex);
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
                $"First-run: Could not validate the concurrently-created durable local config at {path}; " +
                "refusing to generate replacement secrets.", ex);
        }
    }

    private static void EnsureCompleteJsonObject(byte[] payload, string path)
    {
        try
        {
            _ = JsonNode.Parse(payload, nodeOptions: null, documentOptions: LocalConfigJsonOptions)?.AsObject()
                ?? throw new JsonException("The JSON root is null.");

            // Syntax/object shape alone is not enough: the configuration provider flattens keys
            // case-insensitively and rejects collisions such as Connectors + connectors or A:B + A/B.
            // Validate through that exact provider before calling the bytes safe to migrate or load.
            using var stream = new MemoryStream(payload, writable: false);
            _ = new ConfigurationBuilder()
                .AddJsonStream(stream)
                .Build();
        }
        catch (Exception ex) when (
            ex is JsonException or InvalidOperationException or FormatException or InvalidDataException)
        {
            throw new InvalidOperationException(
                $"First-run: The local config at {path} is not a complete JSON object; refusing to " +
                "generate replacement secrets.", ex);
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
    public static WebApplicationBuilder AddLocalConfigFile(
        this WebApplicationBuilder builder,
        string localConfigPath)
    {
        // A present-but-unparsable appsettings.local.json would throw the moment the configuration is built
        // (JsonConfigurationSource.Optional only suppresses a MISSING file, not a malformed one), crashing
        // startup before the first-run checks ever run. Quarantine it first: preserve the corrupt file (it
        // may hold a recoverable key) and remove the original so the optional source loads as "missing" and
        // the desktop install self-heals instead of failing to launch on every restart.
        PrepareLocalConfigFile(
            localConfigPath,
            LegacyLocalConfigPath,
            requireOwnerOnly: builder.Environment.IsProduction() && !IsHeadlessEnvironment());

        // Forward remediation (#1241): an install upgraded from a pre-#1241 build may already have a
        // world-readable appsettings.local.json on disk (the first-run writers return early when the secrets
        // are already present, so PersistValue never re-runs to lock it down). Best-effort re-restrict the
        // existing file to the current user on every startup so existing exposure is healed, not just future
        // writes. Idempotent; never fatal (logged to stderr at this pre-DI stage if it fails).
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
            Path = localConfigPath,
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
    /// config source loads as "missing" instead of throwing at config-build time. Preservation is mandatory:
    /// if no recovery copy can be created, the original stays in place and startup fails closed. A file that
    /// cannot be removed after successful preservation is reported to stderr and remains in place.
    /// </summary>
    internal static void QuarantineCorruptLocalConfig() => QuarantineCorruptLocalConfigAt(LegacyLocalConfigPath);

    /// <summary>Path-parameterized core of <see cref="QuarantineCorruptLocalConfig"/> (testable seam).</summary>
    internal static void QuarantineCorruptLocalConfigAt(
        string path,
        Action<string>? afterCorruptObserved = null,
        Action<string>? restrictFile = null,
        bool requireOwnerOnly = false)
    {
        var normalizedPath = Path.GetFullPath(path);
        using var fileLock = BootstrapFileLock.Acquire(normalizedPath, TimeSpan.FromSeconds(10));
        if (!FileExistsOrThrow(normalizedPath, "local config"))
        {
            return;
        }

        try
        {
            // The original itself becomes the backup via same-directory rename. Production requires
            // owner-only verification before any read; other environments retain the historical
            // best-effort remediation posture. No plain-copy fallback is permitted.
            (restrictFile ?? RestrictFileToCurrentUser)(normalizedPath);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            if (requireOwnerOnly)
            {
                throw new InvalidOperationException(
                    $"First-run: Could not secure local config {normalizedPath} owner-only before inspection; " +
                    "leaving the original untouched and refusing startup.", ex);
            }

            Console.Error.WriteLine(
                $"[FirstRun] WARNING: could not re-restrict permissions on {normalizedPath} before " +
                $"inspection ({ex.Message}); continuing because owner-only enforcement is not required in " +
                "this environment.");
        }

        byte[] observed;
        try
        {
            observed = File.ReadAllBytes(normalizedPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"First-run: Could not read local config {normalizedPath}; leaving it untouched and refusing " +
                "startup because it may contain the only recoverable connector key.", ex);
        }

        try
        {
            EnsureCompleteJsonObject(observed, normalizedPath);
            return;
        }
        catch (InvalidOperationException parseError)
        {
            afterCorruptObserved?.Invoke(normalizedPath);

            if (!FileExistsOrThrow(normalizedPath, "local config"))
            {
                return;
            }

            try
            {
                // A callback or non-cooperating writer may have replaced the path. Secure that CURRENT file
                // before reading it when policy requires; if its bytes differ, it is the winner and must
                // survive untouched.
                (restrictFile ?? RestrictFileToCurrentUser)(normalizedPath);
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                if (requireOwnerOnly)
                {
                    throw new InvalidOperationException(
                        $"First-run: Could not secure the current local config at {normalizedPath} " +
                        "owner-only before quarantine; leaving it untouched and refusing startup.", ex);
                }

                Console.Error.WriteLine(
                    $"[FirstRun] WARNING: could not re-restrict permissions on current local config " +
                    $"{normalizedPath} before quarantine ({ex.Message}); continuing because owner-only " +
                    "enforcement is not required in this environment.");
            }

            try
            {
                var current = File.ReadAllBytes(normalizedPath);
                if (!observed.AsSpan().SequenceEqual(current))
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"First-run: Could not verify the exact corrupt bytes at {normalizedPath}; leaving the " +
                    "current file untouched and refusing startup.", ex);
            }

            var backupPath =
                $"{normalizedPath}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
            try
            {
                // Same-directory rename is atomic and carries the inspected file's current ACL/mode with the
                // exact observed bytes. It cannot expose a plain-copy window or delete before recovery is
                // durable.
                File.Move(normalizedPath, backupPath, overwrite: false);
                var moved = File.ReadAllBytes(backupPath);
                if (!observed.AsSpan().SequenceEqual(moved))
                {
                    if (!File.Exists(normalizedPath))
                    {
                        File.Move(backupPath, normalizedPath, overwrite: false);
                    }

                    throw new InvalidOperationException(
                        $"First-run: The local config at {normalizedPath} changed during atomic quarantine; " +
                        "the replacement was restored and startup is stopping.");
                }

                try
                {
                    (restrictFile ?? RestrictFileToCurrentUser)(backupPath);
                }
                catch (Exception ex) when (
                    ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    if (requireOwnerOnly)
                    {
                        if (File.Exists(backupPath) && !File.Exists(normalizedPath))
                        {
                            try { File.Move(backupPath, normalizedPath, overwrite: false); } catch { /* fail closed */ }
                        }

                        throw new InvalidOperationException(
                            $"First-run: Could not secure the quarantined local config at {backupPath} " +
                            "owner-only; the original was restored when possible and startup is stopping.", ex);
                    }

                    Console.Error.WriteLine(
                        $"[FirstRun] WARNING: could not re-restrict permissions on quarantined local config " +
                        $"{backupPath} ({ex.Message}); the exact recovery bytes were retained, and startup is " +
                        "continuing because owner-only enforcement is not required in this environment.");
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A failed rename leaves the original in place. If a later verification failed after the
                // rename, restore the exact bytes whenever the path is still vacant.
                if (File.Exists(backupPath) && !File.Exists(normalizedPath))
                {
                    try { File.Move(backupPath, normalizedPath, overwrite: false); } catch { /* fail closed */ }
                }

                throw new InvalidOperationException(
                    $"First-run: Could not atomically preserve corrupt local config {normalizedPath}; " +
                    "refusing to delete or replace recoverable bytes.", ex);
            }

            Console.Error.WriteLine(
                $"[FirstRun] WARNING: {normalizedPath} contains invalid JSON ({parseError.Message}). The " +
                $"exact bytes were atomically quarantined at {backupPath} for recovery.");
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
        ILogger logger,
        string localConfigPath)
    {
        var isProduction = builder.Environment.IsProduction();
        var isHeadless = IsHeadlessEnvironment();
        var resolveDatabaseToAppData = !builder.Environment.IsDevelopment() && !isHeadless;
        var databaseAppDataPath = resolveDatabaseToAppData
            ? isProduction
                ? Path.GetDirectoryName(localConfigPath)
                : GetAppDataPath()
            : null;

        // Recovery and the existing-database safety gate must run before EITHER secret is generated. If
        // the database survived but its connector key did not, generating a replacement key would make
        // stored connector credentials permanently unreadable while appearing to repair startup.
        EnsureBootstrapSecrets(
            builder.Configuration,
            logger,
            localConfigPath,
            isProduction,
            isHeadless,
            resolveDatabaseToAppData,
            databaseAppDataPath);

        // Remaining first-run checks are for the self-hosted packaged distribution only -- skip in
        // Development and CI/headless.
        if (!resolveDatabaseToAppData)
        {
            return builder;
        }

        EnsureDbPath(builder.Configuration, logger, localConfigPath, databaseAppDataPath!);
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
                $"key may already be in {localConfigPath} -- reuse that value rather than generating a new " +
                "one, or stored connector credentials will become unrecoverable.)");
        }

        logger.LogInformation("Production secret validation passed.");
        return builder;
    }

    // -------------------------------------------------------------------------

    internal static string LegacyLocalConfigPath
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, LocalConfigFileName));

    // Retained as a test/backward-compatibility alias for the historical executable-local path. Runtime
    // hosts must call ResolveLocalConfigPath once and thread that exact result through every consumer.
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
    /// Recovers a persisted connector key, blocks replacement-secret generation when an existing database
    /// has no recoverable key, then generates the connector key (when policy allows) before the JWT secret.
    /// The ordering is data-loss protection: JWT continuity costs a login, connector-key continuity protects
    /// encrypted stored credentials.
    /// </summary>
    internal static void EnsureBootstrapSecrets(
        IConfiguration configuration,
        ILogger logger,
        string localConfigPath,
        bool isProduction,
        bool isHeadless,
        bool resolveDatabaseToAppData,
        string? databaseAppDataPath = null)
    {
        string? recoveredPersistedKey = null;
        if (string.IsNullOrWhiteSpace(configuration["Connectors:EncryptionKey"])
            && TryReadPersistedConnectorKey(localConfigPath, out recoveredPersistedKey))
        {
            configuration["Connectors:EncryptionKey"] = recoveredPersistedKey;
            logger.LogWarning(
                "First-run: A higher-priority configuration source (likely an empty " +
                "Connectors__EncryptionKey environment variable) is masking the connector key persisted " +
                "in {ConfigFile}. Reusing the persisted key so stored connector credentials stay " +
                "decryptable; unset the empty variable to silence this warning.",
                localConfigPath);
        }

        var databasePath = ResolveDatabaseFilePath(
            configuration,
            resolveDatabaseToAppData,
            databaseAppDataPath);
        if (databasePath is not null
            && FileExistsOrThrow(databasePath, "resolved SQLite database")
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

        // JWT is intentionally second: the existing-database/key guard above must be able to stop startup
        // without writing either replacement secret.
        EnsureJwtSecret(
            configuration,
            logger,
            localConfigPath,
            requirePersistence: isProduction && !isHeadless);

        // IConfigurationRoot.Reload() inside the JWT persistence path can restore an empty higher-priority
        // provider and mask the key recovered above. Re-apply that SAME key in memory; never regenerate it.
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

    internal static void EnsureJwtSecret(
        IConfiguration configuration,
        ILogger logger,
        string localConfigPath,
        bool requirePersistence = false,
        Action? afterInitialAbsence = null,
        TimeSpan? lockTimeout = null,
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
            result = PersistValueUnderLock(
                localConfigPath,
                "Jwt",
                "SecretKey",
                secretFactory ?? GenerateSecret,
                acceptExistingValue: value => !IsPlaceholder(value),
                lockTimeout ?? TimeSpan.FromSeconds(10));
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (requirePersistence)
            {
                throw new InvalidOperationException(
                    $"First-run: Could not persist the JWT secret to {localConfigPath} ({ex.Message}). " +
                    "Non-headless Production refuses a transient secret that would invalidate sessions on " +
                    "restart.", ex);
            }

            var transient = (secretFactory ?? GenerateSecret)();
            configuration["Jwt:SecretKey"] = transient;
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
        TimeSpan? lockTimeout = null,
        Func<string>? secretFactory = null)
    {
        var configured = configuration["Connectors:EncryptionKey"] ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return;
        }

        // The effective value is empty. Before entering the decisive atomic read-or-create section, check
        // whether a key is already persisted on disk.
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

        // A second process can create the key after the optimistic read above. Tests use this seam to force
        // both contenders past that read; the decisive read-or-create remains inside the named mutex below.
        afterInitialAbsence?.Invoke();

        PersistedValueResult result;
        try
        {
            result = PersistValueUnderLock(
                localConfigPath,
                "Connectors",
                "EncryptionKey",
                secretFactory ?? GenerateSecret,
                acceptExistingValue: value => !string.IsNullOrWhiteSpace(value),
                lockTimeout ?? TimeSpan.FromSeconds(10));
            if (configuration is IConfigurationRoot root)
            {
                root.Reload();
            }

            if (result.Created)
            {
                logger.LogInformation(
                    "First-run: Connector encryption key was not configured. A random key has been generated " +
                    "and saved to {ConfigFile}. BACK UP THIS FILE alongside your database -- it is required to " +
                    "decrypt stored connector credentials; losing it makes them unrecoverable.", localConfigPath);
            }
            else
            {
                logger.LogInformation(
                    "First-run: Another startup persisted the connector encryption key in {ConfigFile}. " +
                    "Reusing that winning key.", localConfigPath);
            }
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
            var transient = (secretFactory ?? GenerateSecret)();
            configuration["Connectors:EncryptionKey"] = transient;
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
        // Always install the value chosen under the lock. A reload can leave an empty higher-priority source
        // effective, and a losing contender must never keep its unused candidate in memory.
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

        // Already an absolute path — nothing to do.
        if (!string.IsNullOrWhiteSpace(dataSource) && Path.IsPathRooted(dataSource))
        {
            return;
        }

        Directory.CreateDirectory(appDataPath);

        var dbFile = string.IsNullOrWhiteSpace(dataSource)
            ? "taskdeck.db"
            : Path.GetFileName(dataSource);

        var resolvedPath = Path.Combine(appDataPath, dbFile);
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

        // A command-line, environment, or other higher-priority provider can still expose the original
        // relative value after the JSON reload. Install the exact resolved value for this process so the
        // existing-database safety check above and AddInfrastructure open the same SQLite file.
        configuration["ConnectionStrings:DefaultConnection"] = resolvedConnectionString;

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
        // Environment.GetFolderPath uses the Windows Known Folder API and ignores an overridden
        // LOCALAPPDATA environment variable. Honour the standard variable explicitly on Windows so
        // packaged smoke tests and portable profile launchers can isolate Taskdeck without touching the
        // runner/user's real profile. Normal desktop sessions already point it at the same known folder.
        var localAppData = OperatingSystem.IsWindows()
            ? Environment.GetEnvironmentVariable("LOCALAPPDATA")
            : null;
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.Create);
        }

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

    private static void PersistValue(string path, string section, string key, string value)
        => _ = PersistValueUnderLock(
            path,
            section,
            key,
            () => value,
            acceptExistingValue: null,
            TimeSpan.FromSeconds(10));

    private static PersistedValueResult PersistValueUnderLock(
        string path,
        string section,
        string key,
        Func<string> valueFactory,
        Func<string, bool>? acceptExistingValue,
        TimeSpan lockTimeout)
    {
        // Every API/CLI/quarantine/migration participant acquires this Application-layer primitive. The
        // shared canonical path identity is the contract; a timeout or lock API failure exits before any
        // read, generation, or write.
        using (BootstrapFileLock.Acquire(path, lockTimeout))
        {
            JsonObject root;
            if (FileExistsOrThrow(path, "local config"))
            {
                try
                {
                    // Parse with the config provider's leniency (comments / trailing commas). A hand-edited
                    // but provider-loadable file must round-trip here -- a strict parse would treat it as
                    // corrupt and drop its existing sections (e.g. the Connectors key) when a later first-run
                    // write (EnsureDbPath / JWT) rewrites the file, orphaning stored connector credentials.
                    var existing = File.ReadAllText(path);
                    EnsureCompleteJsonObject(Encoding.UTF8.GetBytes(existing), path);
                    root = JsonNode.Parse(
                            existing,
                            nodeOptions: null,
                            documentOptions: LocalConfigJsonOptions)?.AsObject()
                        ?? throw new JsonException("The local-config JSON root is null.");
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException)
                {
                    throw new InvalidOperationException(
                        $"First-run: Local config {path} is corrupt and may contain recoverable secrets; " +
                        "refusing to write replacement values.", ex);
                }
            }
            else
            {
                root = new JsonObject();
            }

            var hasExistingValue = TryGetJsonString(root, section, key, out var existingValue);
            if (acceptExistingValue is not null
                && hasExistingValue
                && acceptExistingValue(existingValue))
            {
                return new PersistedValueResult(existingValue, Created: false);
            }

            // Microsoft.Extensions.Configuration treats a top-level JSON property containing ':' as the
            // same provider path as its nested representation. Preserve whichever provider-valid form the
            // operator used: adding a nested value beside an existing flattened property would create a
            // duplicate key and make the next provider load fail.
            var flattenedPath = $"{section}:{key}";
            var flattenedPair = root.FirstOrDefault(
                pair => string.Equals(pair.Key, flattenedPath, StringComparison.OrdinalIgnoreCase));
            JsonObject? sectionNode = null;
            if (flattenedPair.Key is null)
            {
                var sectionPair = root.FirstOrDefault(
                    pair => string.Equals(pair.Key, section, StringComparison.OrdinalIgnoreCase));
                if (sectionPair.Key is null)
                {
                    sectionNode = new JsonObject();
                    root[section] = sectionNode;
                }
                else if (sectionPair.Value is JsonObject existingSection)
                {
                    sectionNode = existingSection;
                }
                else
                {
                    throw new InvalidOperationException(
                        $"First-run: Local config {path} contains a non-object {section} section; refusing to " +
                        "overwrite recoverable configuration bytes.");
                }
            }

            // Invoke the factory only after acquiring the lock and proving the value is still absent. This
            // makes creation itself single-winner, not merely the final write.
            var value = valueFactory();
            if (flattenedPair.Key is not null)
            {
                root[flattenedPair.Key] = value;
            }
            else
            {
                var nestedSection = sectionNode
                    ?? throw new InvalidOperationException("First-run: Missing nested persistence target.");
                var existingKeyName = nestedSection.FirstOrDefault(
                    pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Key;
                nestedSection[existingKeyName ?? key] = value;
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            var payload = root.ToJsonString(options);

            // Atomic write: stage into a sibling temp file then move into place.
            // File.WriteAllText is not atomic — a concurrent reader or writer
            // can observe a partially written file.  A rename onto an existing
            // path is atomic on both Windows and Linux file systems we target.
            var dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);
            var tempPath = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

            // Create the temp file ATOMICALLY with owner-only permissions and FileShare.None (#1241/#1264):
            // under the Unix default umask (022) a plainly-created file would be 0644, and on Windows it
            // would inherit the directory's default ACL (e.g. BUILTIN\Users read). Supplying the restrictive
            // mode/DACL at creation leaves no instant where the file is openable by another local user --
            // the previous create-then-restrict sequence had a window where a racer's pre-opened handle on
            // the empty file survived the tightened ACL (ACL changes do not revoke open handles) and could
            // read the later-written secret. FileMode.CreateNew also refuses to adopt a pre-created file.
            // If the restricted create fails, WriteRestrictedFile throws (normalized to IOException) and the
            // secret is never written -- the caller falls back to an in-memory value rather than leaving it
            // world-readable. The create+write -> move sequence is inside the try so any failure deletes the
            // staged temp file rather than leaking it.
            try
            {
                WriteRestrictedFile(tempPath, payload);
                ReplaceFileWithRetry(tempPath, path);
                VerifyFileOwnerOnly(path);
                return new PersistedValueResult(value, Created: true);
            }
            catch
            {
                // Best-effort cleanup; the original exception propagates.
                try { File.Delete(tempPath); } catch { /* ignore */ }
                throw;
            }
        }
    }

    private static bool TryGetJsonString(
        JsonObject root,
        string section,
        string key,
        out string value)
    {
        value = string.Empty;
        var flattenedPath = $"{section}:{key}";
        var flattenedPair = root.FirstOrDefault(
            pair => string.Equals(pair.Key, flattenedPath, StringComparison.OrdinalIgnoreCase));
        if (flattenedPair.Key is not null)
        {
            if (flattenedPair.Value is JsonValue flattenedValue
                && flattenedValue.TryGetValue<string>(out var parsedFlattenedValue)
                && parsedFlattenedValue is not null)
            {
                value = parsedFlattenedValue;
                return true;
            }

            throw new InvalidOperationException(
                $"First-run: Local config contains a null or non-string {flattenedPath} value; refusing to " +
                "overwrite recoverable configuration bytes.");
        }

        var sectionPair = root.FirstOrDefault(
            pair => string.Equals(pair.Key, section, StringComparison.OrdinalIgnoreCase));
        if (sectionPair.Key is null)
        {
            return false;
        }

        if (sectionPair.Value is not JsonObject sectionNode)
        {
            throw new InvalidOperationException(
                $"First-run: Local config contains a null or non-object {section} section; refusing to " +
                "overwrite recoverable configuration bytes.");
        }

        var valuePair = sectionNode.FirstOrDefault(
            pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase));
        if (valuePair.Key is null)
        {
            return false;
        }

        if (valuePair.Value is JsonValue valueNode
            && valueNode.TryGetValue<string>(out var parsedValue)
            && parsedValue is not null)
        {
            value = parsedValue;
            return true;
        }

        throw new InvalidOperationException(
            $"First-run: Local config contains a null or non-string {section}:{key} value; refusing to " +
            "overwrite recoverable configuration bytes.");
    }

    private readonly record struct PersistedValueResult(string Value, bool Created);

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
                    $"First-run: Existing durable local config {path} could not be verified owner-only; " +
                    "refusing to load persisted secrets in non-headless Production.", ex);
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
        => BootstrapFileSecurity.WriteRestrictedFile(path, contents);

    internal static void WriteRestrictedFile(string path, byte[] contents)
        => BootstrapFileSecurity.WriteRestrictedFile(path, contents);

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
        => BootstrapFileSecurity.RestrictFileToCurrentUser(path);

    internal static void VerifyFileOwnerOnly(string path)
        => BootstrapFileSecurity.VerifyFileOwnerOnly(path);

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
        => BootstrapFileLock.BuildMutexName(path);
}
