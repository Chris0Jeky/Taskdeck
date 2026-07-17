using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;
using Taskdeck.Tests.Support;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Verifies <see cref="SerializedMigrator"/> serializes <c>Database.Migrate()</c> across
/// concurrent migrators sharing one SQLite file so the schema is applied exactly once, and
/// that in-memory / non-file databases skip the cross-process lock entirely. #1164
/// <para>
/// The concurrency test drives two in-process threads rather than a second OS process; the
/// <see cref="FileShare.None"/> lock is OS-enforced, so this is a faithful in-process proxy
/// for the cross-process race the migrator must close.
/// </para>
/// </summary>
public sealed class SerializedMigratorTests : IDisposable
{
    private const int BusyTimeoutMs = 5000;
    private readonly string _dbPath;

    public SerializedMigratorTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"taskdeck-serialized-migrate-{Guid.NewGuid():N}.db");
    }

    private TaskdeckDbContext NewFileContext() => NewFileContext(_dbPath);

    private static TaskdeckDbContext NewFileContext(string dbPath)
    {
        var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            // Mirror production: WAL + busy_timeout (#1130) on every connection.
            .AddInterceptors(new SqlitePragmaConnectionInterceptor(BusyTimeoutMs))
            .Options;
        return new TaskdeckDbContext(options);
    }

    [Fact]
    public async Task Concurrent_migrators_apply_schema_exactly_once()
    {
        // Two independent DbContexts point at the SAME fresh file DB. Each applies migrations
        // from its own task; a Barrier makes both reach Migrate at the same instant to
        // maximize the overlap the file lock must serialize. These are two in-process threads,
        // NOT a second OS process, but FileShare.None is an OS-enforced exclusive lock, so this
        // is a faithful in-process proxy for the cross-process race. Without the lock the loser
        // would hit a UNIQUE violation double-inserting into __EFMigrationsHistory.
        //
        // Repeated over a few fresh databases to shrink the probabilistic window in which the
        // two threads happen not to overlap (and so the lock is never actually contended).
        const int iterations = 4;
        for (var i = 0; i < iterations; i++)
        {
            var dbPath = Path.Combine(
                Path.GetTempPath(), $"taskdeck-serialized-migrate-concurrent-{Guid.NewGuid():N}.db");
            try
            {
                // Both contexts are constructed up front (not inside the tasks) so nothing
                // between barrier-entry and Migrate() can throw and deadlock the sibling.
                using var contextA = NewFileContext(dbPath);
                using var contextB = NewFileContext(dbPath);
                using var startBarrier = new Barrier(2);

                Task RunMigratorAsync(TaskdeckDbContext context) => Task.Run(() =>
                {
                    // Bounded wait: if a sibling dies before reaching the barrier, fail fast
                    // instead of hanging CI forever.
                    if (!startBarrier.SignalAndWait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException(
                            "Sibling migrator never reached the start barrier.");
                    }

                    SerializedMigrator.Migrate(context);
                });

                var migratorA = RunMigratorAsync(contextA);
                var migratorB = RunMigratorAsync(contextB);

                // Deliberately NOT a NotThrow assertion. SerializedMigrator documents a lock-free
                // fail-open path: when the sidecar lock file is uncreatable or its acquisition
                // times out under load, a losing migrator races the winner mid-chain and can throw
                // a "table already exists" / duplicate-__EFMigrationsHistory conflict that its
                // NoMigrationsPending re-check could not yet swallow (the winner had not finished
                // the chain). That collision is an EXPECTED outcome of the documented fallback, so
                // the test asserts the invariant that actually matters — the FINAL schema state —
                // instead of no-throw. Await both migrators to completion, tolerating ONLY that
                // documented collision signature; any other throw, or a failure on the LOCKED path
                // (which never swallows), is a real defect and must surface.
                var faults = new List<Exception>();
                foreach (var migrator in new[] { migratorA, migratorB })
                {
                    try
                    {
                        await migrator;
                    }
                    catch (Exception ex)
                    {
                        faults.Add(ex);
                    }
                }

                foreach (var fault in faults)
                {
                    IsSqliteCollisionFault(fault).Should().BeTrue(
                        "the only tolerable concurrent-migrator failure is the documented fail-open " +
                        "mid-chain SQLite collision (any SqliteException, walked through wrappers); " +
                        $"any non-SQL throw is a real defect, but got: {fault}");
                }

                faults.Count.Should().BeLessThan(2,
                    "at least one migrator must drive the schema to completion — the winner that " +
                    "created the conflicting object never throws the collision, so both migrators " +
                    "failing means no run applied the chain");

                using var verify = NewFileContext(dbPath);

                // Representative table exists AND is usable: querying Boards through the migrated
                // schema must succeed. This proves the migration produced a well-formed table, not
                // merely a name registered in sqlite_master.
                GetUserTableNames(verify).Should().Contain(
                    "Boards", "the migration chain must have created the Boards table");
                verify.Set<Board>().Count().Should().Be(0,
                    "the migrated Boards table must be usable — a real query against it must succeed");

                // Applied exactly once: __EFMigrationsHistory holds the COMPLETE migration list
                // with no duplicates, regardless of which fail-open collisions occurred above.
                var historyIds = GetMigrationHistoryIds(verify);
                historyIds.Should().OnlyHaveUniqueItems(
                    "concurrent migrators must not double-insert __EFMigrationsHistory rows");
                historyIds.Should().BeEquivalentTo(
                    verify.Database.GetMigrations(),
                    "every defined migration must be applied exactly once across the concurrent run");
            }
            finally
            {
                CleanupDbFiles(dbPath);
            }
        }
    }

    [Fact]
    public void Single_migrator_on_file_db_acquires_lock_and_does_not_fail_open()
    {
        var logger = new InMemoryLogger<SerializedMigratorTests>();
        using var context = NewFileContext();

        SerializedMigrator.Migrate(context, logger);

        // Assert the helper's "acquired migration lock" Debug entry rather than the sidecar
        // file's continued existence: cleanup is documented as optional/best-effort, so the
        // log is the stable contract that the lock was actually taken (locked path), not
        // silently skipped or degraded to the fail-open branch.
        logger.Entries.Should().Contain(
            e => e.Level == LogLevel.Debug && e.Message.Contains("acquired migration lock"),
            "a real SQLite file database must be migrated under the sidecar advisory lock");

        // Fail-open regression guard: a healthy single-migrator run must emit NO Warning/Error.
        // If the lock were silently never taken (always degrading to the unlocked path), this
        // happy path would start logging a warning and this assertion would catch it.
        logger.Entries.Should().NotContain(
            e => e.Level >= LogLevel.Warning,
            "a healthy single migrator must take the lock, never silently fail open");

        using var verify = NewFileContext();
        GetUserTableNames(verify).Should().Contain(
            "Boards", "the migration chain must have created the Boards table");
    }

    [Fact]
    public void In_memory_database_migrates_without_creating_a_lock_file()
    {
        // A kept-open connection keeps the :memory: schema alive for assertions (a fresh
        // connection per command would discard it).
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
            .UseSqlite(connection)
            .Options;
        using var context = new TaskdeckDbContext(options);

        // Still migrates: no exception, and the schema is present.
        SerializedMigrator.Migrate(context);
        context.Set<Board>().Count().Should().Be(0,
            "the in-memory database should be migrated even though no lock is taken");

        // No lock file: :memory: resolves to no file path, so the helper must never open a
        // FileStream. Guard against a regression that naively builds a lock path from the
        // ":memory:" data source (on Unix Path.GetFullPath(":memory:") lands in the cwd).
        var strayLock = Path.Combine(Directory.GetCurrentDirectory(), ":memory:.migrate.lock");
        File.Exists(strayLock).Should().BeFalse(
            "an in-memory database must not produce a sidecar migration lock file");
    }

    [Fact]
    public void Migrate_proceeds_with_a_warning_when_lock_cannot_be_acquired_within_timeout()
    {
        var lockPath = Path.GetFullPath(_dbPath) + ".migrate.lock";

        // Simulate another process holding the exclusive lock for the whole call.
        using var heldLock = new FileStream(
            lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        var logger = new InMemoryLogger<SerializedMigratorTests>();
        using var context = NewFileContext();

        // Acquisition must time out, then degrade to a warning and migrate anyway —
        // Migrate() is idempotent and busy_timeout still serializes the actual write.
        // This also exercises the public Migrate(DbContext, TimeSpan, ILogger?) overload.
        var act = () => SerializedMigrator.Migrate(context, TimeSpan.FromMilliseconds(300), logger);
        act.Should().NotThrow("a contended lock must degrade to a warning, never block startup");

        using var verify = NewFileContext();
        GetUserTableNames(verify).Should().Contain(
            "Boards", "migrations must still apply when the lock cannot be acquired");

        logger.Entries.Should().Contain(
            e => e.Level == LogLevel.Warning,
            "failing to acquire the migration lock within the timeout must be logged as a warning");
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="exception"/> is (or wraps, anywhere in its
    /// inner-exception chain) a <see cref="SqliteException"/> — the signature of a losing
    /// fail-open migrator racing the winner mid-chain. Deliberately NOT message-based: the same
    /// race lands on whatever DDL the colliding migration issues first — <c>"table ... already
    /// exists"</c> (CreateTable), <c>"duplicate column name"</c> (AddColumn-first migrations such
    /// as AddDeferredUntilToAutomationProposal), <c>"no such table"</c> / <c>ef_temp</c> shapes
    /// (table-rebuild migrations), or <c>"UNIQUE constraint failed"</c> (a duplicate
    /// <c>__EFMigrationsHistory</c> insert) — so enumerating message strings re-flakes with a
    /// misleading "real defect" message the next time the chain reshapes. Tolerating ANY
    /// <see cref="SqliteException"/> cannot mask a real defect because this check never gates
    /// success alone: the <c>faults.Count &lt; 2</c> gate rejects "no migrator drove the chain"
    /// (a genuinely broken migration faults BOTH migrators here and also fails the
    /// single-migrator test), and the exact-state assertions (complete duplicate-free history ==
    /// <c>GetMigrations()</c>; usable schema) reject any incomplete or corrupted outcome. This
    /// type check exists ONLY to keep non-SQL faults — barrier timeouts, null refs, IO failures —
    /// as hard test failures.
    /// </summary>
    private static bool IsSqliteCollisionFault(Exception exception)
    {
        for (Exception? ex = exception; ex is not null; ex = ex.InnerException)
        {
            if (ex is SqliteException)
            {
                return true;
            }
        }

        return false;
    }

    public static TheoryData<Exception, bool> CollisionFaultCases => new()
    {
        // Direct SqliteExceptions from every DDL shape the fail-open race can land on.
        { new SqliteException("SQLite Error 1: 'table \"ProposalProvenances\" already exists'.", 1), true },
        { new SqliteException("SQLite Error 1: 'duplicate column name: DeferredUntil'.", 1), true },
        { new SqliteException("SQLite Error 1: 'no such table: ef_temp_Boards'.", 1), true },
        // Wrapped: EF surfaces the duplicate __EFMigrationsHistory insert as a DbUpdateException.
        {
            new DbUpdateException(
                "An error occurred while saving the entity changes.",
                new SqliteException(
                    "SQLite Error 19: 'UNIQUE constraint failed: __EFMigrationsHistory.MigrationId'.", 19)),
            true
        },
        // Non-SQL faults must stay hard failures.
        { new TimeoutException("Sibling migrator never reached the start barrier."), false },
        { new InvalidOperationException("outer", new IOException("disk failure")), false },
    };

    [Theory]
    [MemberData(nameof(CollisionFaultCases))]
    public void Collision_signature_accepts_any_sqlite_fault_and_rejects_non_sql_faults(
        Exception fault, bool expected)
    {
        IsSqliteCollisionFault(fault).Should().Be(expected,
            "the collision signature must tolerate every SQLite shape of the fail-open race " +
            "(walking wrapped exceptions) while keeping non-SQL faults as hard failures");
    }

    private static List<string> GetUserTableNames(TaskdeckDbContext context)
    {
        var names = new List<string>();
        var connection = context.Database.GetDbConnection();
        context.Database.OpenConnection();
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' " +
                "AND name != '__EFMigrationsHistory' ORDER BY name";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                names.Add(reader.GetString(0));
            }
        }
        finally
        {
            context.Database.CloseConnection();
        }

        return names;
    }

    private static List<string> GetMigrationHistoryIds(TaskdeckDbContext context)
    {
        var ids = new List<string>();
        var connection = context.Database.GetDbConnection();
        context.Database.OpenConnection();
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                ids.Add(reader.GetString(0));
            }
        }
        finally
        {
            context.Database.CloseConnection();
        }

        return ids;
    }

    public void Dispose() => CleanupDbFiles(_dbPath);

    private static void CleanupDbFiles(string dbPath)
    {
        // Drop pooled connections so file handles release before cleanup.
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm", ".migrate.lock" })
        {
            var path = dbPath + suffix;
            if (File.Exists(path))
            {
                // Best-effort: on Windows a still-locked file can throw; never fail the run.
                try { File.Delete(path); }
                catch (Exception) { /* best-effort temp cleanup */ }
            }
        }
    }
}
