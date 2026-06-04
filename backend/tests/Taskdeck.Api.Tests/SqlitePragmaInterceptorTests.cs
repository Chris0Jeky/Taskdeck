using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Verifies <see cref="SqlitePragmaConnectionInterceptor"/> applies the local-first
/// concurrency PRAGMAs (WAL + busy_timeout) and that two DbContexts sharing one SQLite
/// file can write concurrently without hitting SQLITE_BUSY ("database is locked"). #1130
/// </summary>
public sealed class SqlitePragmaInterceptorTests : IDisposable
{
    private const int BusyTimeoutMs = 5000;
    private readonly string _dbPath;

    public SqlitePragmaInterceptorTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"taskdeck-pragma-test-{Guid.NewGuid():N}.db");
    }

    private TaskdeckDbContext NewContext() => NewContext(BusyTimeoutMs);

    private TaskdeckDbContext NewContext(int busyTimeoutMs)
    {
        var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .AddInterceptors(new SqlitePragmaConnectionInterceptor(busyTimeoutMs))
            .Options;
        return new TaskdeckDbContext(options);
    }

    private static T ScalarPragma<T>(TaskdeckDbContext context, string pragma)
    {
        var connection = context.Database.GetDbConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {pragma};";
        var result = command.ExecuteScalar();
        return (T)Convert.ChangeType(result!, typeof(T));
    }

    [Fact]
    public void Interceptor_enables_wal_and_busy_timeout_on_connection_open()
    {
        using var context = NewContext();
        // Open through EF's pipeline so the DbConnectionInterceptor fires (a raw
        // DbConnection.Open() would bypass it).
        context.Database.OpenConnection();
        try
        {
            ScalarPragma<string>(context, "journal_mode").Should().BeEquivalentTo("wal");
            ScalarPragma<long>(context, "busy_timeout").Should().Be(BusyTimeoutMs);
        }
        finally
        {
            context.Database.CloseConnection();
        }
    }

    [Fact]
    public async Task Two_contexts_can_write_to_the_same_file()
    {
        using (var init = NewContext())
        {
            init.Database.Migrate();
        }

        // Coexistence smoke test: two independent contexts each commit a write to the
        // same WAL file. (The load-bearing guards are the PRAGMA-verification test above
        // and the busy_timeout contention test below — a fast single-row insert does not
        // hold the writer slot long enough to reliably provoke contention on its own.)
        using (var contextA = NewContext())
        {
            contextA.Set<Board>().Add(new Board("write-A"));
            await contextA.SaveChangesAsync();
        }
        using (var contextB = NewContext())
        {
            contextB.Set<Board>().Add(new Board("write-B"));
            await contextB.SaveChangesAsync();
        }

        using var verify = NewContext();
        verify.Set<Board>().Count().Should().Be(2);
    }

    [Fact]
    public async Task Writer_without_busy_timeout_fails_fast_under_a_held_write_lock()
    {
        using (var init = NewContext())
        {
            init.Database.Migrate();
        }

        // Hold an exclusive write lock on a separate raw connection.
        await using var lockConnection = new SqliteConnection($"Data Source={_dbPath}");
        await lockConnection.OpenAsync();
        await using (var begin = lockConnection.CreateCommand())
        {
            begin.CommandText = "BEGIN IMMEDIATE;";
            await begin.ExecuteNonQueryAsync();
        }

        // With busy_timeout=0 a contended writer fails immediately with SQLITE_BUSY.
        // This proves busy_timeout is load-bearing: the interceptor's default (5000ms)
        // is precisely what makes a real writer wait for the lock instead of erroring.
        using var blocked = NewContext(busyTimeoutMs: 0);
        blocked.Set<Board>().Add(new Board("blocked"));
        var act = async () => await blocked.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();

        await using var rollback = lockConnection.CreateCommand();
        rollback.CommandText = "ROLLBACK;";
        await rollback.ExecuteNonQueryAsync();
    }

    public void Dispose()
    {
        // Drop pooled connections so the file handles release before cleanup.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = _dbPath + suffix;
            if (File.Exists(path))
            {
                // Best-effort: on Windows a still-locked file can throw IOException or
                // UnauthorizedAccessException; neither should fail the test run.
                try { File.Delete(path); }
                catch (Exception) { /* best-effort temp cleanup */ }
            }
        }
    }
}
