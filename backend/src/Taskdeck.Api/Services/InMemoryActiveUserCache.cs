using System.Collections.Concurrent;
using Taskdeck.Application.Interfaces;

namespace Taskdeck.Api.Services;

/// <summary>
/// Thread-safe, in-memory cache for user active-status checks.
/// Registered as a singleton. Entries expire after a configurable TTL (default 30 seconds).
/// Stale entries are lazily evicted on access; a periodic sweep is unnecessary for the
/// expected cardinality of a local-first app.
/// </summary>
public sealed class InMemoryActiveUserCache : IActiveUserCache
{
    private readonly ConcurrentDictionary<Guid, CacheEntry> _cache = new();
    private readonly TimeSpan _ttl;

    public InMemoryActiveUserCache(TimeSpan? ttl = null)
    {
        _ttl = ttl ?? TimeSpan.FromSeconds(30);
    }

    public bool? GetCachedActiveStatus(Guid userId)
    {
        if (!_cache.TryGetValue(userId, out var entry))
            return null;

        if (entry.ExpiresAtUtc < DateTime.UtcNow)
        {
            // Expired — remove and report miss
            _cache.TryRemove(userId, out _);
            return null;
        }

        return entry.IsActive;
    }

    public void SetActiveStatus(Guid userId, bool isActive)
    {
        var entry = new CacheEntry(isActive, DateTime.UtcNow.Add(_ttl));
        _cache[userId] = entry;
    }

    public void Invalidate(Guid userId)
    {
        _cache.TryRemove(userId, out _);
    }

    /// <summary>
    /// Returns the current number of entries in the cache (for diagnostics/testing).
    /// </summary>
    internal int Count => _cache.Count;

    private sealed record CacheEntry(bool IsActive, DateTime ExpiresAtUtc);
}
