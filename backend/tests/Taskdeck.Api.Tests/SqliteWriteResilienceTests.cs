using FluentAssertions;
using Microsoft.Data.Sqlite;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

public sealed class SqliteWriteResilienceTests
{
    [Fact]
    public async Task ExecuteWithWriteLockRetryAsync_ShouldRetryBusyFailures_ThenReturnResult()
    {
        var attempts = 0;

        var result = await SqliteWriteResilience.ExecuteWithWriteLockRetryAsync<int>(async _ =>
        {
            attempts++;
            await Task.Yield();
            if (attempts < 3)
                throw new InvalidOperationException("wrap", new SqliteException("SQLite Error 5: 'database is locked'.", 5));
            return 7;
        });

        result.Should().Be(7);
        attempts.Should().Be(3);
    }

    [Fact]
    public async Task ExecuteWithWriteLockRetryAsync_ShouldRetryLockMessage_WithoutSqliteException()
    {
        var attempts = 0;

        var result = await SqliteWriteResilience.ExecuteWithWriteLockRetryAsync<int>(async _ =>
        {
            attempts++;
            await Task.Yield();
            if (attempts == 1)
                throw new InvalidOperationException("database table is locked");
            return 1;
        });

        result.Should().Be(1);
        attempts.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteWithWriteLockRetryAsync_ShouldNotRetry_NonTransientFailures()
    {
        var attempts = 0;

        var act = () => SqliteWriteResilience.ExecuteWithWriteLockRetryAsync<int>(_ =>
        {
            attempts++;
            throw new InvalidOperationException("constraint failed");
#pragma warning disable CS0162 // Unreachable: the stub always throws; required for the delegate shape.
            return Task.FromResult(0);
#pragma warning restore CS0162
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteWithWriteLockRetryAsync_ShouldGiveUp_AfterFiveRetries()
    {
        var attempts = 0;

        var act = () => SqliteWriteResilience.ExecuteWithWriteLockRetryAsync<int>(_ =>
        {
            attempts++;
            throw new SqliteException("SQLite Error 5: 'database is locked'.", 5);
#pragma warning disable CS0162 // Unreachable: the stub always throws; required for the delegate shape.
            return Task.FromResult(0);
#pragma warning restore CS0162
        });

        await act.Should().ThrowAsync<SqliteException>();
        attempts.Should().Be(6);
    }
}
