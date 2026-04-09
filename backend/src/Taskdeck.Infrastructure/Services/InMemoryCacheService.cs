using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Taskdeck.Application.Interfaces;

namespace Taskdeck.Infrastructure.Services;

/// <summary>
/// In-memory cache implementation for local dev and test environments.
/// Thread-safe via ConcurrentDictionary. Entries expire lazily on access.
/// Periodic sweep prevents unbounded memory growth.
/// </summary>
public sealed class InMemoryCacheService : ICacheService, IDisposable
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly ILogger<InMemoryCacheService> _logger;
    private readonly string _keyPrefix;
    private readonly Timer _sweepTimer;

    /// <summary>
    /// Maximum cache entries before forced eviction of expired entries.
    /// </summary>
    private const int MaxEntries = 10_000;

    public InMemoryCacheService(ILogger<InMemoryCacheService> logger, string keyPrefix = "td")
    {
        _logger = logger;
        _keyPrefix = keyPrefix;

        // Sweep expired entries every 60 seconds
        _sweepTimer = new Timer(_ => SweepExpiredEntries(), null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
    }

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        var fullKey = BuildKey(key);

        try
        {
            if (!_cache.TryGetValue(fullKey, out var entry))
            {
                _logger.LogDebug("Cache miss for key {CacheKey}", fullKey);
                LogCacheMetric("miss", key);
                return Task.FromResult<T?>(null);
            }

            if (entry.ExpiresAtUtc < DateTime.UtcNow)
            {
                _cache.TryRemove(fullKey, out _);
                _logger.LogDebug("Cache expired for key {CacheKey}", fullKey);
                LogCacheMetric("miss", key);
                return Task.FromResult<T?>(null);
            }

            var value = JsonSerializer.Deserialize<T>(entry.SerializedValue);
            _logger.LogDebug("Cache hit for key {CacheKey}", fullKey);
            LogCacheMetric("hit", key);
            return Task.FromResult(value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache get error for key {CacheKey}", fullKey);
            LogCacheMetric("error", key);
            return Task.FromResult<T?>(null);
        }
    }

    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default) where T : class
    {
        var fullKey = BuildKey(key);

        try
        {
            if (_cache.Count >= MaxEntries)
            {
                SweepExpiredEntries();
            }

            var serialized = JsonSerializer.Serialize(value);
            var entry = new CacheEntry(serialized, DateTime.UtcNow.Add(ttl));
            _cache[fullKey] = entry;
            _logger.LogDebug("Cache set for key {CacheKey} with TTL {Ttl}s", fullKey, ttl.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache set error for key {CacheKey}", fullKey);
            LogCacheMetric("error", key);
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var fullKey = BuildKey(key);

        try
        {
            _cache.TryRemove(fullKey, out _);
            _logger.LogDebug("Cache removed key {CacheKey}", fullKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache remove error for key {CacheKey}", fullKey);
            LogCacheMetric("error", key);
        }

        return Task.CompletedTask;
    }

    public Task RemoveByPrefixAsync(string keyPrefix, CancellationToken cancellationToken = default)
    {
        var fullPrefix = BuildKey(keyPrefix);

        try
        {
            var keysToRemove = _cache.Keys.Where(k => k.StartsWith(fullPrefix, StringComparison.Ordinal)).ToList();
            foreach (var k in keysToRemove)
            {
                _cache.TryRemove(k, out _);
            }

            _logger.LogDebug("Cache removed {Count} keys with prefix {CacheKeyPrefix}", keysToRemove.Count, fullPrefix);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache remove by prefix error for {CacheKeyPrefix}", fullPrefix);
            LogCacheMetric("error", keyPrefix);
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _sweepTimer.Dispose();
        _cache.Clear();
    }

    /// <summary>
    /// Returns the current number of entries in the cache (for diagnostics/testing).
    /// </summary>
    internal int Count => _cache.Count;

    private string BuildKey(string key) => $"{_keyPrefix}:{key}";

    private void SweepExpiredEntries()
    {
        var now = DateTime.UtcNow;
        var swept = 0;
        foreach (var kvp in _cache)
        {
            if (kvp.Value.ExpiresAtUtc < now)
            {
                if (_cache.TryRemove(kvp.Key, out _))
                    swept++;
            }
        }

        if (swept > 0)
        {
            _logger.LogDebug("Cache sweep removed {SweptCount} expired entries", swept);
        }
    }

    private void LogCacheMetric(string outcome, string keyPrefix)
    {
        // Extract the resource type from the key for tagging (e.g., "boards" from "boards:user:...")
        var resource = keyPrefix.Split(':').FirstOrDefault() ?? "unknown";
        _logger.LogDebug("CacheMetric outcome={Outcome} resource={Resource}", outcome, resource);
    }

    private sealed record CacheEntry(string SerializedValue, DateTime ExpiresAtUtc);
}
