using FluentAssertions;
using Taskdeck.Infrastructure.Services;
using Xunit;

namespace Taskdeck.Api.Tests;

public class NoOpCacheServiceTests
{
    private readonly NoOpCacheService _cache = NoOpCacheService.Instance;

    [Fact]
    public async Task GetAsync_AlwaysReturnsNull()
    {
        var result = await _cache.GetAsync<string>("anykey");
        result.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_DoesNotThrow()
    {
        await _cache.SetAsync("key", "value", TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task RemoveAsync_DoesNotThrow()
    {
        await _cache.RemoveAsync("key");
    }

    [Fact]
    public async Task RemoveByPrefixAsync_DoesNotThrow()
    {
        await _cache.RemoveByPrefixAsync("prefix:");
    }

    [Fact]
    public void Instance_IsSingleton()
    {
        var instance1 = NoOpCacheService.Instance;
        var instance2 = NoOpCacheService.Instance;
        instance1.Should().BeSameAs(instance2);
    }
}
