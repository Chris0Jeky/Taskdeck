using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Taskdeck.Application.Interfaces;

namespace Taskdeck.Infrastructure.Services;

/// <summary>
/// Redis-backed cache implementation for production/multi-instance deployments.
/// All operations degrade safely on connection failure — no exceptions propagated to callers.
/// Uses StackExchange.Redis with reconnection support — transient Redis outages do not
/// permanently disable the cache.
/// </summary>
public sealed class RedisCacheService : ICacheService, IDisposable
{
    private readonly string _connectionString;
    private readonly ILogger<RedisCacheService> _logger;
    private readonly string _keyPrefix;
    private readonly object _connectionLock = new();
    private volatile ConnectionMultiplexer? _connection;
    private volatile bool _disposed;

    /// <summary>
    /// Minimum interval between reconnection attempts to avoid reconnection storms.
    /// </summary>
    private static readonly TimeSpan ReconnectMinInterval = TimeSpan.FromSeconds(15);

    private DateTime _lastConnectAttemptUtc = DateTime.MinValue;

    public RedisCacheService(string connectionString, ILogger<RedisCacheService> logger, string keyPrefix = "td")
    {
        _connectionString = connectionString;
        _logger = logger;
        _keyPrefix = keyPrefix;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        var fullKey = BuildKey(key);
        var db = GetDatabase();
        if (db is null)
        {
            LogCacheMetric("miss", key); // Connection down counts as miss
            return null;
        }

        try
        {
            var value = await db.StringGetAsync(fullKey);
            if (value.IsNullOrEmpty)
            {
                _logger.LogDebug("Cache miss for key {CacheKey}", fullKey);
                LogCacheMetric("miss", key);
                return null;
            }

            var result = JsonSerializer.Deserialize<T>(value.ToString());
            _logger.LogDebug("Cache hit for key {CacheKey}", fullKey);
            LogCacheMetric("hit", key);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache get error for key {CacheKey}", fullKey);
            LogCacheMetric("error", key);
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default) where T : class
    {
        var fullKey = BuildKey(key);
        var db = GetDatabase();
        if (db is null) return;

        try
        {
            var serialized = JsonSerializer.Serialize(value);
            await db.StringSetAsync(fullKey, serialized, ttl);
            _logger.LogDebug("Cache set for key {CacheKey} with TTL {Ttl}s", fullKey, ttl.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache set error for key {CacheKey}", fullKey);
            LogCacheMetric("error", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var fullKey = BuildKey(key);
        var db = GetDatabase();
        if (db is null) return;

        try
        {
            await db.KeyDeleteAsync(fullKey);
            _logger.LogDebug("Cache removed key {CacheKey}", fullKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache remove error for key {CacheKey}", fullKey);
            LogCacheMetric("error", key);
        }
    }

    public async Task RemoveByPrefixAsync(string keyPrefix, CancellationToken cancellationToken = default)
    {
        var fullPrefix = BuildKey(keyPrefix);
        var connection = GetConnection();
        if (connection is null) return;

        try
        {
            // Use SCAN to find keys by prefix — safe for production (non-blocking).
            // Process keys in batches to avoid materializing all keys into memory at once.
            const int batchSize = 100;
            var endpoints = connection.GetEndPoints();
            var db = connection.GetDatabase();

            foreach (var endpoint in endpoints)
            {
                var server = connection.GetServer(endpoint);
                var keys = new List<RedisKey>(batchSize);
                var totalRemoved = 0;

                // Stream keys using IEnumerable — Keys() uses SCAN internally
                foreach (var key in server.Keys(pattern: $"{fullPrefix}*"))
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    keys.Add(key);
                    if (keys.Count >= batchSize)
                    {
                        await db.KeyDeleteAsync(keys.ToArray());
                        totalRemoved += keys.Count;
                        keys.Clear();
                    }
                }

                // Delete any remaining keys
                if (keys.Count > 0)
                {
                    await db.KeyDeleteAsync(keys.ToArray());
                    totalRemoved += keys.Count;
                }

                if (totalRemoved > 0)
                {
                    _logger.LogDebug("Cache removed {Count} keys with prefix {CacheKeyPrefix}", totalRemoved, fullPrefix);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache remove by prefix error for {CacheKeyPrefix}", fullPrefix);
            LogCacheMetric("error", keyPrefix);
        }
    }

    public void Dispose()
    {
        // Take the connection lock so a concurrent GetConnection() publish cannot assign a
        // freshly-established multiplexer after we read _connection, which would leak it.
        ConnectionMultiplexer? toDispose;
        lock (_connectionLock)
        {
            if (_disposed) return;
            _disposed = true;
            toDispose = _connection;
            _connection = null;
        }

        try
        {
            toDispose?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Exception during Redis connection disposal");
        }
    }

    private IDatabase? GetDatabase()
    {
        if (_disposed) return null;

        try
        {
            var connection = GetConnection();
            if (connection is null || !connection.IsConnected)
            {
                return null;
            }
            return connection.GetDatabase();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis connection unavailable — operating in degraded mode");
            return null;
        }
    }

    /// <summary>
    /// Gets or establishes the Redis connection. A single connection attempt is made per
    /// <see cref="ReconnectMinInterval"/> window so transient Redis outages do not permanently
    /// disable caching, while the throttle prevents reconnection storms. On failure the method
    /// returns <c>null</c> immediately so callers fall through to the no-cache path — it never
    /// throws and never blocks while holding <see cref="_connectionLock"/> for the connect
    /// (the lock guards only the minimal check-and-assign of <see cref="_connection"/>).
    /// </summary>
    private ConnectionMultiplexer? GetConnection()
    {
        if (_disposed) return null;

        var conn = _connection;
        if (conn is not null && conn.IsConnected)
            return conn;

        // Throttle reconnection attempts to avoid storms. A single attempt per window means a
        // failed Connect degrades to no-cache immediately rather than blocking on retries.
        if (DateTime.UtcNow - _lastConnectAttemptUtc < ReconnectMinInterval)
            return conn; // Return stale connection (or null) — don't retry yet

        // Hold the lock only around the minimal check-and-assign so a single connecting thread
        // updates shared state while concurrent callers fall straight through to the no-cache path.
        if (!Monitor.TryEnter(_connectionLock))
            return conn; // Another thread is already attempting — don't pile up.

        ConnectionMultiplexer? old;
        try
        {
            if (_disposed) return null;

            conn = _connection;
            if (conn is not null && conn.IsConnected)
                return conn;

            if (DateTime.UtcNow - _lastConnectAttemptUtc < ReconnectMinInterval)
                return conn;

            _lastConnectAttemptUtc = DateTime.UtcNow;

            // Capture the (possibly broken) old connection to dispose after we release the lock.
            old = _connection;
        }
        finally
        {
            Monitor.Exit(_connectionLock);
        }

        // Single connection attempt performed OUTSIDE the lock: the 3s ConnectTimeout must never
        // block other callers, and the outer throttle already guarantees at most one attempt per
        // ReconnectMinInterval window.
        ConnectionMultiplexer? established = null;
        try
        {
            var options = ConfigurationOptions.Parse(_connectionString);
            options.AbortOnConnectFail = false;  // Allow startup without Redis
            options.ConnectTimeout = 3000;       // 3 second connect timeout
            options.SyncTimeout = 1000;          // 1 second sync timeout
            options.AsyncTimeout = 1000;         // 1 second async timeout
            var candidate = ConnectionMultiplexer.Connect(options);

            if (candidate.IsConnected)
            {
                established = candidate;
            }
            else
            {
                // With AbortOnConnectFail=false, Connect returns a non-connected multiplexer
                // instead of throwing. Discard it and surface degraded mode to operators.
                candidate.Dispose();
                _logger.LogWarning("Redis cache connection unavailable — operating in degraded (no-cache) mode");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis cache connection failed — operating in degraded (no-cache) mode");
        }

        // Publish the result under the lock (minimal check-and-assign only).
        lock (_connectionLock)
        {
            if (_disposed)
            {
                established?.Dispose();
                return null;
            }

            // Another thread may have established a live connection while we were connecting.
            var current = _connection;
            if (current is not null && current.IsConnected && !ReferenceEquals(current, established))
            {
                established?.Dispose();
                return current;
            }

            if (established is not null)
            {
                _connection = established;
                _logger.LogInformation("Redis cache connected successfully");
            }
            else
            {
                _connection = null;
            }
        }

        // Dispose the previous broken connection outside the lock (best-effort cleanup).
        if (old is not null && !ReferenceEquals(old, established))
        {
            try { old.Dispose(); }
            catch (Exception) { /* best-effort cleanup */ }
        }

        return established;
    }

    private string BuildKey(string key) => $"{_keyPrefix}:{key}";

    private void LogCacheMetric(string outcome, string keyPrefix)
    {
        var resource = keyPrefix.Split(':').FirstOrDefault() ?? "unknown";
        _logger.LogDebug("CacheMetric outcome={Outcome} resource={Resource}", outcome, resource);
    }
}
