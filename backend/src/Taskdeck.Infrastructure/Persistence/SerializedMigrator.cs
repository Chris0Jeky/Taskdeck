using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Taskdeck.Application.Services;

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
/// the lock directory is unwritable, a warning is logged and migration proceeds anyway.
/// On that lock-free path a concurrent migrator can still win a logical DDL race
/// (<c>"table already exists"</c>, or a duplicate <c>__EFMigrationsHistory</c> insert):
/// <c>busy_timeout</c> only serializes <c>SQLITE_BUSY</c>, <b>not</b> these logical conflicts.
/// So the unlocked <c>Migrate()</c> is wrapped — a throw is swallowed only when a re-check of
/// <see cref="RelationalDatabaseFacadeExtensions.GetPendingMigrations"/> shows nothing remains
/// pending (the racing winner already applied everything); otherwise the failure propagates.
/// </para>
/// <para>
/// <b>Pre-migration backup (#1803).</b> When migrations are actually pending against an
/// existing SQLite file, a consistent snapshot of that file is written first via
/// <see cref="SqlitePreMigrationBackup"/> — inside the lock, before <c>Migrate()</c> opens any
/// DDL write connection. That backup is <b>fail-closed</b>: if it cannot be written, the
/// migration does not run and the failure propagates out of startup. Everything else here is
/// deliberately fail-open (a coordination file is best-effort), but a schema rewrite without a
/// recovery copy is not a risk this local-first app takes on the user's behalf.
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
        => Migrate(context, DefaultLockTimeout, backupSettings: null, logger);

    /// <summary>
    /// Applies migrations for <paramref name="context"/> under a cross-process file lock,
    /// waiting at most <paramref name="lockTimeout"/> to acquire it before proceeding anyway.
    /// </summary>
    public static void Migrate(DbContext context, TimeSpan lockTimeout, ILogger? logger = null)
        => Migrate(context, lockTimeout, backupSettings: null, logger);

    /// <summary>
    /// Applies migrations for <paramref name="context"/> under a cross-process file lock, taking
    /// a pre-migration snapshot of the SQLite file governed by <paramref name="backupSettings"/>.
    /// This is the overload the hosts use, so the backup honours configuration (#1803).
    /// </summary>
    public static void Migrate(
        DbContext context,
        DatabaseBackupSettings? backupSettings,
        ILogger? logger = null)
        => Migrate(context, DefaultLockTimeout, backupSettings, logger);

    /// <summary>
    /// Applies migrations for <paramref name="context"/> under a cross-process file lock,
    /// waiting at most <paramref name="lockTimeout"/> to acquire it before proceeding anyway,
    /// and taking a pre-migration snapshot governed by <paramref name="backupSettings"/>
    /// (<c>null</c> means "use the defaults", i.e. backups enabled).
    /// </summary>
    public static void Migrate(
        DbContext context,
        TimeSpan lockTimeout,
        DatabaseBackupSettings? backupSettings,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        var databaseFilePath = ResolveDatabaseFilePath(context, logger);
        if (databaseFilePath is null)
        {
            // In-memory, non-file, or non-SQLite database: nothing to coordinate across
            // processes, and no file to snapshot. Apply migrations directly.
            context.Database.Migrate();
            return;
        }

        var lockPath = databaseFilePath + LockFileSuffix;

        FileStream? lockStream = null;
        try
        {
            lockStream = AcquireLock(lockPath, lockTimeout, logger);
            if (lockStream is not null)
            {
                // Locked path: we hold the exclusive advisory lock, so no other migrator can be
                // mutating the schema concurrently. Any Migrate() throw here is a genuine failure
                // — let it propagate untouched.
                logger?.LogDebug("SerializedMigrator: acquired migration lock '{LockPath}'.", lockPath);
                BackupBeforeMigrating(context, databaseFilePath, backupSettings, logger);
                context.Database.Migrate();
            }
            else
            {
                // Fail-open path: we could NOT take the lock (timed out, or the lock file was
                // uncreatable). A concurrent migrator may still be applying the schema, so this
                // Migrate() can race it. Guard the throw rather than crash startup.
                //
                // The BACKUP is still fail-closed here. A lost coordination file is survivable;
                // rewriting the schema with no recovery copy is not, and the backup API snapshots
                // a committed state, so a concurrent migrator does not make the snapshot invalid —
                // it only makes it slightly older or slightly newer than this process expected.
                BackupBeforeMigrating(context, databaseFilePath, backupSettings, logger);
                MigrateWithoutLock(context, logger);
            }
        }
        finally
        {
            lockStream?.Dispose();
        }
    }

    /// <summary>
    /// Snapshots the SQLite file before any DDL runs, but only when it is worth doing: backups
    /// enabled, the file already exists (a fresh install has nothing to protect), and migrations
    /// are genuinely pending (an ordinary boot with an up-to-date schema must not copy the
    /// database on every start).
    /// <para>
    /// If the pending set cannot be read at all, the backup is taken anyway — "I could not tell"
    /// is treated as "there might be", because the whole point is to be conservative here.
    /// </para>
    /// </summary>
    private static void BackupBeforeMigrating(
        DbContext context,
        string databaseFilePath,
        DatabaseBackupSettings? backupSettings,
        ILogger? logger)
    {
        var settings = backupSettings ?? new DatabaseBackupSettings();
        if (!settings.Enabled)
        {
            logger?.LogDebug(
                "SerializedMigrator: pre-migration backup is disabled (Database:Backup:Enabled=false); " +
                "migrating '{DatabasePath}' without a snapshot.",
                databaseFilePath);
            return;
        }

        if (!File.Exists(databaseFilePath))
        {
            // First run: Migrate() is about to create the file. There is nothing to lose yet.
            return;
        }

        if (!MayHavePendingMigrations(context, logger))
        {
            return;
        }

        SqlitePreMigrationBackup.Create(databaseFilePath, settings, logger);
    }

    /// <summary>
    /// Returns <c>false</c> only when EF definitively reports an empty pending set. Any failure
    /// to read it returns <c>true</c> so the backup is still taken.
    /// </summary>
    private static bool MayHavePendingMigrations(DbContext context, ILogger? logger)
    {
        try
        {
            return context.Database.GetPendingMigrations().Any();
        }
        catch (Exception ex)
        {
            logger?.LogWarning(
                ex,
                "SerializedMigrator: could not determine whether migrations are pending ({Reason}); " +
                "taking a pre-migration backup anyway.",
                ex.GetType().Name);
            return true;
        }
    }

    /// <summary>
    /// Applies migrations on the lock-free fallback path, where a concurrent migrator may win a
    /// logical DDL race. EF computes the pending set <em>before</em> taking SQLite's write lock,
    /// so a racing loser can throw <see cref="Microsoft.Data.Sqlite.SqliteException"/>
    /// (<c>"table already exists"</c>) or a <see cref="DbUpdateException"/> from a duplicate
    /// <c>__EFMigrationsHistory</c> primary-key insert. <c>busy_timeout</c> only serializes
    /// <c>SQLITE_BUSY</c>, not these conflicts. The exception filter re-checks the pending set:
    /// if it is now empty the racing winner applied everything and the throw is swallowed as a
    /// no-op; otherwise migrations genuinely remain unapplied and the failure propagates with its
    /// original stack trace.
    /// </summary>
    private static void MigrateWithoutLock(DbContext context, ILogger? logger)
    {
        try
        {
            context.Database.Migrate();
        }
        catch (Exception ex) when (NoMigrationsPending(context))
        {
            logger?.LogWarning(
                ex,
                "SerializedMigrator: applied migrations without the cross-process lock and raced " +
                "a concurrent migrator ({Reason}); all migrations are now present, so the conflict " +
                "is treated as a successful no-op.",
                ex.GetType().Name);
        }
    }

    /// <summary>
    /// Returns <c>true</c> only when EF reports zero pending migrations — i.e. another process
    /// already applied everything. If the pending set cannot be read, returns <c>false</c> so the
    /// caller's exception filter declines to swallow the original failure.
    /// </summary>
    private static bool NoMigrationsPending(DbContext context)
    {
        try
        {
            return !context.Database.GetPendingMigrations().Any();
        }
        catch
        {
            // Could not confirm the schema is fully applied: do not mask the real failure.
            return false;
        }
    }

    /// <summary>
    /// Resolves the absolute path of the context's SQLite database file, or <c>null</c> when
    /// there is no such file (non-SQLite provider, in-memory database, or a data source that is
    /// not a usable file path). The sidecar lock path is this plus
    /// <see cref="LockFileSuffix"/>, and the pre-migration backup snapshots this file.
    /// </summary>
    private static string? ResolveDatabaseFilePath(DbContext context, ILogger? logger)
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
            var filePath = dataSource!;

            // SQLite also accepts URI data sources (e.g. "file:taskdeck.db?cache=shared").
            // Handing the raw URI to Path.GetFullPath throws on Windows (the '?' is an invalid
            // path char) or, on Unix, produces a lock file with a literal '?' in its name.
            // Extract the local file path first so both spellings of the same file coordinate
            // on one lock. (Pure ":memory:" URIs are already filtered out above.)
            if (Uri.TryCreate(filePath, UriKind.Absolute, out var uri) && uri.IsFile)
            {
                filePath = uri.LocalPath;
            }

            // Normalize so two processes that pass the same relative "Data Source=taskdeck.db"
            // from the same working directory resolve to the same absolute lock path.
            //
            // LIMITATION: Path.GetFullPath is a LEXICAL normalization only — it collapses
            // "."/".." and applies the current directory, but does NOT canonicalize symlinks,
            // junctions, or hard links. Two differently-spelled paths that resolve to the same
            // physical file (e.g. a symlink and its target, or 8.3 vs long Windows names) key to
            // DIFFERENT lock files and are therefore NOT serialized against each other. That
            // residual race is bounded by the WAL + busy_timeout writer fallback and the
            // unlocked-Migrate re-check, and is acceptable for the local-first SQLite scenario.
            return Path.GetFullPath(filePath);
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
                "applying migrations without a cross-process lock and without a pre-migration backup.",
                dataSource);
            return null;
        }
    }

    /// <summary>
    /// Opens the lock file exclusively (<see cref="FileShare.None"/>), retrying with a short
    /// backoff while another process holds it. Returns the held stream, or <c>null</c> if the
    /// lock could not be acquired within <paramref name="timeout"/> or the file could not be
    /// created at all (in which case the caller proceeds without the lock).
    /// <para>
    /// <c>internal</c> (not <c>private</c>) so the lock-exclusivity contract — while one
    /// acquisition holds, a second must fail open; after release, it must succeed — stays
    /// directly regression-tested (#1164): a <see cref="FileShare.None"/> weakening or early
    /// stream disposal would otherwise pass every state-based migration test unnoticed.
    /// Exposed via the existing <c>InternalsVisibleTo("Taskdeck.Api.Tests")</c>.
    /// </para>
    /// </summary>
    internal static FileStream? AcquireLock(string lockPath, TimeSpan timeout, ILogger? logger)
    {
        // Monotonic clock (Stopwatch), not DateTime.UtcNow: a wall-clock step (NTP correction,
        // DST, manual change) must not skew how long we are willing to wait for the lock.
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
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
            catch (IOException) when (elapsed.Elapsed < timeout)
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
                    "The unlocked Migrate() re-checks the pending set and swallows a concurrent " +
                    "migrator's conflict only if nothing remains pending — busy_timeout alone only " +
                    "serializes SQLITE_BUSY, not logical DDL conflicts.",
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
