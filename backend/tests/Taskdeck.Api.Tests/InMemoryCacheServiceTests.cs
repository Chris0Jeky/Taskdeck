using FluentAssertions;
using Taskdeck.Infrastructure.Services;
using Taskdeck.Tests.Support;
using Xunit;

namespace Taskdeck.Api.Tests;

public class InMemoryCacheServiceTests : IDisposable
{
    private readonly InMemoryLogger<InMemoryCacheService> _logger;
    private readonly InMemoryCacheService _cache;

    public InMemoryCacheServiceTests()
    {
        _logger = new InMemoryLogger<InMemoryCacheService>();
        _cache = new InMemoryCacheService(_logger, "test");
    }

    public void Dispose()
    {
        _cache.Dispose();
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_OnCacheMiss()
    {
        var result = await _cache.GetAsync<TestData>("nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_ThenGetAsync_ReturnsCachedValue()
    {
        var data = new TestData("hello", 42);
        await _cache.SetAsync("key1", data, TimeSpan.FromMinutes(5));

        var result = await _cache.GetAsync<TestData>("key1");

        result.Should().NotBeNull();
        result!.Name.Should().Be("hello");
        result.Value.Should().Be(42);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_AfterExpiry()
    {
        var data = new TestData("expiring", 1);
        await _cache.SetAsync("expkey", data, TimeSpan.Zero);

        await Task.Delay(10);

        var result = await _cache.GetAsync<TestData>("expkey");
        result.Should().BeNull();
    }

    [Fact]
    public async Task RemoveAsync_RemovesCachedEntry()
    {
        var data = new TestData("removeme", 99);
        await _cache.SetAsync("rmkey", data, TimeSpan.FromMinutes(5));

        await _cache.RemoveAsync("rmkey");

        var result = await _cache.GetAsync<TestData>("rmkey");
        result.Should().BeNull();
    }

    [Fact]
    public async Task RemoveByPrefixAsync_RemovesMatchingEntries()
    {
        await _cache.SetAsync("boards:user:a", new TestData("a", 1), TimeSpan.FromMinutes(5));
        await _cache.SetAsync("boards:user:b", new TestData("b", 2), TimeSpan.FromMinutes(5));
        await _cache.SetAsync("other:key", new TestData("c", 3), TimeSpan.FromMinutes(5));

        await _cache.RemoveByPrefixAsync("boards:user:");

        (await _cache.GetAsync<TestData>("boards:user:a")).Should().BeNull();
        (await _cache.GetAsync<TestData>("boards:user:b")).Should().BeNull();
        (await _cache.GetAsync<TestData>("other:key")).Should().NotBeNull();
    }

    [Fact]
    public async Task SetAsync_OverwritesExistingEntry()
    {
        await _cache.SetAsync("key", new TestData("old", 1), TimeSpan.FromMinutes(5));
        await _cache.SetAsync("key", new TestData("new", 2), TimeSpan.FromMinutes(5));

        var result = await _cache.GetAsync<TestData>("key");
        result.Should().NotBeNull();
        result!.Name.Should().Be("new");
        result.Value.Should().Be(2);
    }

    [Fact]
    public async Task RemoveAsync_IsIdempotent_DoesNotThrow()
    {
        await _cache.RemoveAsync("nonexistent");

        await _cache.SetAsync("key", new TestData("x", 1), TimeSpan.FromMinutes(5));
        await _cache.RemoveAsync("key");
        await _cache.RemoveAsync("key");
    }

    [Fact]
    public async Task RemoveByPrefixAsync_IsIdempotent_WhenNoMatchingKeys()
    {
        await _cache.RemoveByPrefixAsync("nonexistent:prefix:");
    }

    [Fact]
    public async Task GetAsync_LogsHitAndMissMetrics()
    {
        await _cache.SetAsync("metrickey", new TestData("test", 1), TimeSpan.FromMinutes(5));

        await _cache.GetAsync<TestData>("missing");
        await _cache.GetAsync<TestData>("metrickey");

        var debugLogs = _logger.Entries
            .Where(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Debug)
            .Select(e => e.Message)
            .ToList();

        debugLogs.Should().Contain(m => m.Contains("outcome=miss"));
        debugLogs.Should().Contain(m => m.Contains("outcome=hit"));
    }

    [Fact]
    public async Task KeysAreIsolatedByPrefix()
    {
        using var cache2 = new InMemoryCacheService(_logger, "other");

        await _cache.SetAsync("shared", new TestData("from-test", 1), TimeSpan.FromMinutes(5));
        await cache2.SetAsync("shared", new TestData("from-other", 2), TimeSpan.FromMinutes(5));

        var result1 = await _cache.GetAsync<TestData>("shared");
        var result2 = await cache2.GetAsync<TestData>("shared");

        result1!.Name.Should().Be("from-test");
        result2!.Name.Should().Be("from-other");
    }

    [Fact]
    public async Task Count_ReflectsActiveEntries()
    {
        _cache.Count.Should().Be(0);

        await _cache.SetAsync("a", new TestData("a", 1), TimeSpan.FromMinutes(5));
        await _cache.SetAsync("b", new TestData("b", 2), TimeSpan.FromMinutes(5));

        _cache.Count.Should().Be(2);

        await _cache.RemoveAsync("a");

        _cache.Count.Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentAccess_DoesNotThrow()
    {
        var tasks = Enumerable.Range(0, 100).Select(async i =>
        {
            var key = $"concurrent:{i}";
            await _cache.SetAsync(key, new TestData($"data-{i}", i), TimeSpan.FromMinutes(5));
            var result = await _cache.GetAsync<TestData>(key);
            result.Should().NotBeNull();
            await _cache.RemoveAsync(key);
        });

        await Task.WhenAll(tasks);
    }

    private sealed record TestData(string Name, int Value);
}
