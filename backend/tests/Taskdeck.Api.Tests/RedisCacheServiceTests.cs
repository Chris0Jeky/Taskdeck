using System.Diagnostics;
using FluentAssertions;
using Taskdeck.Infrastructure.Services;
using Taskdeck.Tests.Support;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Behavior tests for <see cref="RedisCacheService"/> when Redis is unreachable.
/// These assert the regression fixed in #1189: a failed connection degrades to the
/// no-cache path without throwing, and the single connect attempt is NOT performed while
/// holding the connection lock (so concurrent callers are not serialized behind a
/// multi-second blocking connect, which previously starved the thread pool).
/// </summary>
public sealed class RedisCacheServiceTests : IDisposable
{
    // Loopback + a port that nothing listens on so Connect fails fast with connection refused.
    // AbortOnConnectFail=false (set by the service) means Connect returns a non-connected
    // multiplexer rather than throwing, exercising the degrade-to-no-cache path.
    private const string UnreachableRedis = "127.0.0.1:1,connectRetry=0,abortConnect=false";

    private readonly InMemoryLogger<RedisCacheService> _logger = new();
    private readonly RedisCacheService _cache;

    public RedisCacheServiceTests()
    {
        _cache = new RedisCacheService(UnreachableRedis, _logger, "test");
    }

    public void Dispose() => _cache.Dispose();

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenRedisUnreachable()
    {
        var result = await _cache.GetAsync<TestData>("anykey");

        result.Should().BeNull("an unreachable Redis must degrade to a cache miss, not throw");
    }

    [Fact]
    public async Task SetAsync_DoesNotThrow_WhenRedisUnreachable()
    {
        // Must complete without throwing — writes silently no-op when the cache is down.
        await _cache.SetAsync("key", new TestData("v", 1), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task RemoveAsync_DoesNotThrow_WhenRedisUnreachable()
    {
        await _cache.RemoveAsync("key");
    }

    [Fact]
    public async Task RemoveByPrefixAsync_DoesNotThrow_WhenRedisUnreachable()
    {
        await _cache.RemoveByPrefixAsync("prefix:");
    }

    [Fact]
    public async Task FailedConnect_DegradesToNoCache_AndLogsWarningWithoutThrowing()
    {
        // First call triggers the single connect attempt; it must surface a degraded-mode warning
        // (not an exception) and return the no-cache result.
        var result = await _cache.GetAsync<TestData>("warm");

        result.Should().BeNull();

        var warnings = _logger.Entries
            .Where(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning)
            .Select(e => e.Message)
            .ToList();

        warnings.Should().Contain(m => m.Contains("degraded", StringComparison.OrdinalIgnoreCase),
            "a failed connect should log a degraded-mode warning rather than throw");
    }

    [Fact]
    public async Task ConcurrentCallers_AreNotSerializedBehindBlockingConnect()
    {
        // Regression guard for #1189: the connect attempt (up to a 3s timeout) must NOT be
        // performed while holding _connectionLock. If it were, N concurrent callers would each
        // block on the lock for the full connect duration and total wall-clock time would scale
        // with N. With the lock held only around the minimal check-and-assign, the throttle lets
        // exactly one thread attempt the connect while the rest fall straight through to no-cache,
        // so total time stays close to a single connect attempt.
        const int callers = 16;

        var sw = Stopwatch.StartNew();

        var tasks = Enumerable.Range(0, callers).Select(i => Task.Run(async () =>
        {
            // Mix of operations all routed through GetConnection/GetDatabase.
            var got = await _cache.GetAsync<TestData>($"k{i}");
            got.Should().BeNull();
            await _cache.SetAsync($"k{i}", new TestData("v", i), TimeSpan.FromMinutes(1));
        }));

        var act = async () => await Task.WhenAll(tasks);

        await act.Should().NotThrowAsync("unreachable Redis must never surface exceptions to callers");

        sw.Stop();

        // A single connect attempt is bounded by the 3s ConnectTimeout. If the connect were
        // serialized under the lock, 16 callers could take many multiples of that. We assert the
        // aggregate stays within roughly two connect windows — generous enough to avoid CI
        // flakiness but far below the serialized-blocking failure mode.
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(8),
            "concurrent callers must not each block for the full connect timeout under the lock");
    }

    [Fact]
    public async Task Dispose_IsIdempotent_AndSubsequentCallsDegradeWithoutThrowing()
    {
        // Establish the degraded state, then dispose. Dispose takes the connection lock, so a
        // concurrent connect cannot leak a multiplexer past disposal; calling it twice must no-op.
        await _cache.GetAsync<TestData>("warm");

        _cache.Dispose();
        _cache.Dispose(); // second call must be a no-op, not throw

        // After disposal the service stays in the no-cache path and never throws.
        var result = await _cache.GetAsync<TestData>("after-dispose");
        result.Should().BeNull();
        await _cache.SetAsync("after-dispose", new TestData("v", 1), TimeSpan.FromMinutes(1));
    }

    private sealed record TestData(string Name, int Value);
}
