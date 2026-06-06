using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Taskdeck.Infrastructure.Persistence;

/// <summary>
/// Applies EF Core migrations under a portable, cross-process advisory lock so that
/// multiple Taskdeck hosts (API, MCP-http, MCP-stdio, CLI) booting against the same
/// SQLite file do not race to apply migrations on <c>__EFMigrationsHistory</c>.
/// <para>
/// #1130 enabled <c>WAL + busy_timeout</c> (a racing writer now waits instead of failing
/// immediately with <c>SQLITE_BUSY</c>), which substantially mitigates the race. #1164 is
/// the remaining acceptance criterion: actually <em>serialize</em> migration application so
/// concurrent startups apply the schema exactly once.
/// </para>
/// <para>
/// <b>Mechanism.</b> A sidecar lock file <c>"&lt;dbpath&gt;.migrate.lock"</c> is opened with
/// <see cref="FileShare.None"/>; only one process can hold it at a time. The winner applies
/// migrations; every other process blocks on the lock and, once it acquires it, sees the
/// migrations already applied and no-ops (<see cref="RelationalDatabaseFacadeExtensions.Migrate"/>
/// is idempotent). A .NET named <see cref="System.Threading.Mutex"/> is deliberately avoided:
/// it is not cross-process on Unix.
/// </para>
/// <para>
/// <b>Fallbacks (never block startup).</b> Non-relational/in-memory databases
/// (<c>:memory:</c>, <c>Mode=Memory</c>, the EF Core InMemory provider) and non-SQLite
/// providers skip the lock entirely — there is no shared file to coordinate on, and tests /
/// ephemeral databases must not stall. If the lock cannot be acquired within the timeout, or
/// the lock directory is unwritable, a warning is logged and migration proceeds anyway:
/// <c>Migrate()</c> remains idempotent and <c>busy_timeout</c> still makes a racing writer wait.
/// </para>
/// </summary>
public static class SerializedMigrator
{
    /// <summary>Default upper bound on how long to wait for the migration lock.</summary>
    public static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);

    private const string LockFileSuffix = ".migrate.lock";

    /// <summary>
    /// Applies migrations for <paramref name="context"/> under a cross-process file lock
    /// (when the database is a real SQLite file). See the type remarks for fallback behavior.
    /// </summary>
    /// <param name="context">The context whose database should be migrated.</param>
    /// <param name="logger">Optional logger for diagnostics. Pass <c>null</c> on the CLI path
    /// (CLI stdout must stay clean JSON; nothing is written to stdout here, but callers may
    /// still prefer to suppress logging).</param>
    public static void Migrate(DbContext context, ILogger? logger = null)
        => Migrate(context, DefaultLockTimeout, logger);

    /// <summary>
    /// Applies migrations for <paramref name="context"/> under a cross-process file lock,
    /// waiting at most <paramref name="lockTimeout"/> to acquire it before proceeding anyway.
    /// </summary>
    public static void Migrate(DbContext context, TimeSpan lockTimeout, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        var lockPath = ResolveLockPath(context, logger);
        if (lockPath is null)
        {
            // In-memory, non-file, or non-SQLite database: nothing to coordinate across
            // processes. Apply migrations directly.
            context.Database.Migrate();
            return;
        }

        FileStream? lockStream = null;
        try
        {
            lockStream = AcquireLock(lockPath, lockTimeout, logger);
            if (lockStream is not null)
            {
                logger?.LogDebug("SerializedMigrator: acquired migration lock '{LockPath}'.", lockPath);
            }

            context.Database.Migrate();
        }
        finally
        {
            lockStream?.Dispose();
        }
    }

    /// <summary>
    /// Resolves the sidecar lock-file path for the context's SQLite data source, or
    /// <c>null</c> when no cross-process lock is applicable (non-SQLite provider, in-memory
    /// database, or a data source that is not a usable file path).
    /// </summary>
    private static string? ResolveLockPath(DbContext context, ILogger? logger)
    {
        // Only SQLite file databases need this coordination. A future PostgreSQL runtime
        // (ADR-0023) handles concurrent migrators with server-side locking, and the EF Core
        // InMemory provider has no shared file at all.
        if (!IsSqliteProvider(context))
        {
            return null;
        }

        var connectionString = TryGetConnectionString(context);
        var dataSource = TryGetDataSource(context);

        if (string.IsNullOrWhiteSpace(dataSource) && !string.IsNullOrWhiteSpace(connectionString))
        {
            dataSource = TryParseDataSource(connectionString);
        }

        if (IsNonFileDataSource(dataSource, connectionString))
        {
            return null;
        }

        try
        {
            // Normalize so two processes that pass the same relative "Data Source=taskdeck.db"
            // from the same working directory resolve to the same absolute lock path.
            return Path.GetFullPath(dataSource!) + LockFileSuffix;
        }
        catch (Exception ex) when (
            ex is ArgumentException
                or NotSupportedException
                or PathTooLongException
                or System.Security.SecurityException)
        {
            logger?.LogWarning(
                ex,
                "SerializedMigrator: data source '{DataSource}' is not a usable file path; " +
                "applying migrations without a cross-process lock.",
                dataSource);
            return null;
        }
    }

    /// <summary>
    /// Opens the lock file exclusively (<see cref="FileShare.None"/>), retrying with a short
    /// backoff while another process holds it. Returns the held stream, or <c>null</c> if the
    /// lock could not be acquired within <paramref name="timeout"/> or the file could not be
    /// created at all (in which case the caller proceeds without the lock).
    /// </summary>
    private static FileStream? AcquireLock(string lockPath, TimeSpan timeout, ILogger? logger)
    {
        var deadline = DateTime.UtcNow + timeout;
        var attempts = 0;

        while (true)
        {
            attempts++;
            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                // Another process (or thread) holds the exclusive lock; wait and retry.
                Thread.Sleep(RetryDelay);
            }
            catch (IOException ex)
            {
                logger?.LogWarning(
                    ex,
                    "SerializedMigrator: could not acquire migration lock '{LockPath}' within " +
                    "{TimeoutSeconds}s after {Attempts} attempts; applying migrations without it. " +
                    "Migrate() is idempotent and busy_timeout still serializes the write.",
                    lockPath,
                    timeout.TotalSeconds,
                    attempts);
                return null;
            }
            catch (Exception ex) when (
                ex is UnauthorizedAccessException
                    or DirectoryNotFoundException
                    or NotSupportedException)
            {
                // The lock directory is unwritable / the path is unusable. Do not block
                // startup over a best-effort coordination file.
                logger?.LogWarning(
                    ex,
                    "SerializedMigrator: migration lock file '{LockPath}' could not be created " +
                    "({Reason}); applying migrations without a cross-process lock.",
                    lockPath,
                    ex.GetType().Name);
                return null;
            }
        }
    }

    private static bool IsSqliteProvider(DbContext context)
    {
        try
        {
            return context.Database.IsSqlite();
        }
        catch
        {
            // IsSqlite() can throw when multiple providers are configured; treat the
            // ambiguous case as "do not lock" so we never block startup.
            return false;
        }
    }

    private static string? TryGetDataSource(DbContext context)
    {
        try
        {
            return context.Database.GetDbConnection().DataSource;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetConnectionString(DbContext context)
    {
        try
        {
            return context.Database.GetConnectionString();
        }
        catch
        {
            return null;
        }
    }

    private static string? TryParseDataSource(string connectionString)
    {
        try
        {
            return new SqliteConnectionStringBuilder(connectionString).DataSource;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool IsNonFileDataSource(string? dataSource, string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(dataSource))
        {
            return true;
        }

        // Covers ":memory:" and the shared form "file::memory:?cache=shared".
        if (dataSource.Contains(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Mode=Memory makes any data source (even a named one) an in-memory database.
        if (!string.IsNullOrEmpty(connectionString) &&
            connectionString.Contains("Mode=Memory", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
