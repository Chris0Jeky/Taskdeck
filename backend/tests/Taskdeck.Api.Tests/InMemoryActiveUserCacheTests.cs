using FluentAssertions;
using Taskdeck.Api.Services;
using Xunit;

namespace Taskdeck.Api.Tests;

public class InMemoryActiveUserCacheTests
{
    [Fact]
    public void GetCachedActiveStatus_ReturnsNull_WhenNoCachedEntry()
    {
        var cache = new InMemoryActiveUserCache();
        var result = cache.GetCachedActiveStatus(Guid.NewGuid());
        result.Should().BeNull();
    }

    [Fact]
    public void SetActiveStatus_And_GetCachedActiveStatus_ReturnsTrue_ForActiveUser()
    {
        var cache = new InMemoryActiveUserCache();
        var userId = Guid.NewGuid();

        cache.SetActiveStatus(userId, true);

        cache.GetCachedActiveStatus(userId).Should().BeTrue();
    }

    [Fact]
    public void SetActiveStatus_And_GetCachedActiveStatus_ReturnsFalse_ForInactiveUser()
    {
        var cache = new InMemoryActiveUserCache();
        var userId = Guid.NewGuid();

        cache.SetActiveStatus(userId, false);

        cache.GetCachedActiveStatus(userId).Should().BeFalse();
    }

    [Fact]
    public void Invalidate_RemovesCachedEntry()
    {
        var cache = new InMemoryActiveUserCache();
        var userId = Guid.NewGuid();

        cache.SetActiveStatus(userId, true);
        cache.GetCachedActiveStatus(userId).Should().BeTrue();

        cache.Invalidate(userId);

        cache.GetCachedActiveStatus(userId).Should().BeNull();
    }

    [Fact]
    public void Invalidate_DoesNotThrow_WhenNoEntryExists()
    {
        var cache = new InMemoryActiveUserCache();
        var act = () => cache.Invalidate(Guid.NewGuid());
        act.Should().NotThrow();
    }

    [Fact]
    public void GetCachedActiveStatus_ReturnsNull_WhenEntryExpired()
    {
        // Use a very short TTL so the entry expires immediately
        var cache = new InMemoryActiveUserCache(ttl: TimeSpan.FromMilliseconds(1));
        var userId = Guid.NewGuid();

        cache.SetActiveStatus(userId, true);

        // Wait for expiry
        Thread.Sleep(50);

        cache.GetCachedActiveStatus(userId).Should().BeNull();
    }

    [Fact]
    public void SetActiveStatus_OverwritesPreviousEntry()
    {
        var cache = new InMemoryActiveUserCache();
        var userId = Guid.NewGuid();

        cache.SetActiveStatus(userId, true);
        cache.GetCachedActiveStatus(userId).Should().BeTrue();

        cache.SetActiveStatus(userId, false);
        cache.GetCachedActiveStatus(userId).Should().BeFalse();
    }

    [Fact]
    public void Cache_IsolatesEntries_PerUserId()
    {
        var cache = new InMemoryActiveUserCache();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        cache.SetActiveStatus(userA, true);
        cache.SetActiveStatus(userB, false);

        cache.GetCachedActiveStatus(userA).Should().BeTrue();
        cache.GetCachedActiveStatus(userB).Should().BeFalse();
    }
}
