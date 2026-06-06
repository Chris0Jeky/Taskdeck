using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Verifies <see cref="SerializedMigrator"/> serializes <c>Database.Migrate()</c> across
/// concurrent migrators sharing one SQLite file so the schema is applied exactly once, and
/// that in-memory / non-file databases skip the cross-process lock entirely. #1164
/// </summary>
public sealed class SerializedMigratorTests : IDisposable
{
    private const int BusyTimeoutMs = 5000;
    private readonly string _dbPath;

    public SerializedMigratorTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"taskdeck-serialized-migrate-{Guid.NewGuid():N}.db");
    }

    private TaskdeckDbContext NewFileContext()
    {
        var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            // Mirror production: WAL + busy_timeout (#1130) on every connection.
            .AddInterceptors(new SqlitePragmaConnectionInterceptor(BusyTimeoutMs))
            .Options;
        return new TaskdeckDbContext(options);
    }

    [Fact]
    public async Task Two_concurrent_migrators_apply_schema_exactly_once()
    {
        // Two independent DbContexts point at the SAME fresh file DB. Each applies migrations
        // from its own task; a Barrier makes both reach Migrate at the same instant to
        // maximize the overlap the file lock must serialize. Without the lock the loser would
        // hit a UNIQUE violation double-inserting into __EFMigrationsHistory.
        using var startBarrier = new Barrier(2);

        async Task RunMigratorAsync()
        {
            await Task.Yield();
            using var context = NewFileContext();
            startBarrier.SignalAndWait();
            SerializedMigrator.Migrate(context);
        }

        var migratorA = Task.Run(RunMigratorAsync);
        var migratorB = Task.Run(RunMigratorAsync);

        var act = async () => await Task.WhenAll(migratorA, migratorB);
        await act.Should().NotThrowAsync(
            "the cross-process file lock must serialize concurrent migrators so neither fails");

        using var verify = NewFileContext();

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

    [Fact]
    public void Single_migrator_on_file_db_creates_sidecar_lock_file()
    {
        using var context = NewFileContext();

        SerializedMigrator.Migrate(context);

        // The helper leaves the sidecar lock file in place (cleanup is optional/best-effort).
        // Its presence documents the lock path resolution: "<dbpath>.migrate.lock".
        var expectedLockPath = Path.GetFullPath(_dbPath) + ".migrate.lock";
        File.Exists(expectedLockPath).Should().BeTrue(
            "a real SQLite file database should be migrated under a sidecar advisory lock");
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

    public void Dispose()
    {
        // Drop pooled connections so file handles release before cleanup.
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm", ".migrate.lock" })
        {
            var path = _dbPath + suffix;
            if (File.Exists(path))
            {
                // Best-effort: on Windows a still-locked file can throw; never fail the run.
                try { File.Delete(path); }
                catch (Exception) { /* best-effort temp cleanup */ }
            }
        }
    }
}
