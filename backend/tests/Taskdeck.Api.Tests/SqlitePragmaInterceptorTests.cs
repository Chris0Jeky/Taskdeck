using FluentAssertions;
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

    private TaskdeckDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<TaskdeckDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .AddInterceptors(new SqlitePragmaConnectionInterceptor(BusyTimeoutMs))
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
    public async Task Two_contexts_writing_concurrently_both_succeed_under_wal()
    {
        using (var init = NewContext())
        {
            init.Database.Migrate();
        }

        using var contextA = NewContext();
        using var contextB = NewContext();

        // Two independent contexts (separate connections) write at the same time.
        // SQLite serializes the single writer slot; busy_timeout makes the loser
        // wait for the lock instead of failing immediately, so both commits land.
        var writeA = Task.Run(async () =>
        {
            contextA.Set<Board>().Add(new Board("concurrent-A"));
            await contextA.SaveChangesAsync();
        });
        var writeB = Task.Run(async () =>
        {
            contextB.Set<Board>().Add(new Board("concurrent-B"));
            await contextB.SaveChangesAsync();
        });

        var act = async () => await Task.WhenAll(writeA, writeB);
        await act.Should().NotThrowAsync("WAL + busy_timeout should let concurrent writers serialize instead of erroring with SQLITE_BUSY");

        using var verify = NewContext();
        verify.Set<Board>().Count().Should().Be(2);
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
                try { File.Delete(path); }
                catch (IOException) { /* best-effort temp cleanup */ }
            }
        }
    }
}
