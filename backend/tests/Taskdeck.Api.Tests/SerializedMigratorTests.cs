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

                var act = async () => await Task.WhenAll(migratorA, migratorB);
                await act.Should().NotThrowAsync(
                    "the advisory file lock must serialize concurrent migrators so neither fails");

                using var verify = NewFileContext(dbPath);

                // Schema applied: a known table exists.
                GetUserTableNames(verify).Should().Contain(
                    "Boards", "the migration chain must have created the Boards table");

                // Applied exactly once: every defined migration appears exactly once in history.
                var historyIds = GetMigrationHistoryIds(verify);
                historyIds.Should().OnlyHaveUniqueItems(
                    "two migrators must not double-insert __EFMigrationsHistory rows");
                historyIds.Should().BeEquivalentTo(
                    verify.Database.GetMigrations(),
                    "every defined migration should be applied exactly once across the concurrent run");
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
