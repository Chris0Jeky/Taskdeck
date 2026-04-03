using System.Collections.Concurrent;
using Taskdeck.Application.Interfaces;

namespace Taskdeck.Api.Services;

/// <summary>
/// Thread-safe, in-memory cache for user active-status checks.
/// Registered as a singleton. Entries expire after a configurable TTL (default 30 seconds).
/// Stale entries are lazily evicted on access and via an opportunistic sweep that runs
/// periodically on write to prevent unbounded growth from users who never return.
/// </summary>
public sealed class InMemoryActiveUserCache : IActiveUserCache
{
    private readonly ConcurrentDictionary<Guid, CacheEntry> _cache = new();
    private readonly TimeSpan _ttl;

    /// <summary>
    /// Maximum number of entries before a sweep is attempted on the next write.
    /// Kept generous — a local-first app rarely exceeds a handful of users.
    /// </summary>
    private const int SweepThreshold = 1000;

    private long _writesSinceSweep;

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

        // Opportunistic sweep: every SweepThreshold writes, purge expired entries
        // to prevent unbounded dictionary growth from inactive users.
        if (Interlocked.Increment(ref _writesSinceSweep) >= SweepThreshold)
        {
            Interlocked.Exchange(ref _writesSinceSweep, 0);
            SweepExpiredEntries();
        }
    }

    public void Invalidate(Guid userId)
    {
        _cache.TryRemove(userId, out _);
    }

    /// <summary>
    /// Returns the current number of entries in the cache (for diagnostics/testing).
    /// </summary>
    internal int Count => _cache.Count;

    private void SweepExpiredEntries()
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in _cache)
        {
            if (kvp.Value.ExpiresAtUtc < now)
            {
                _cache.TryRemove(kvp.Key, out _);
            }
        }
    }

    private sealed record CacheEntry(bool IsActive, DateTime ExpiresAtUtc);
}
