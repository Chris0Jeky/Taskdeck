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

    /// <summary>
    /// Maximum number of immediate retry attempts when establishing a connection.
    /// </summary>
    private const int MaxConnectionRetries = 3;

    /// <summary>
    /// Base delay between connection retry attempts (doubles with each retry).
    /// </summary>
    private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromMilliseconds(500);

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
        if (_disposed) return;
        _disposed = true;

        try
        {
            _connection?.Dispose();
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
    /// Gets or establishes the Redis connection. Unlike the previous Lazy-based approach,
    /// this retries connection on failure (with a backoff interval) so transient Redis
    /// outages do not permanently disable caching.
    /// </summary>
    private ConnectionMultiplexer? GetConnection()
    {
        if (_disposed) return null;

        var conn = _connection;
        if (conn is not null && conn.IsConnected)
            return conn;

        // Throttle reconnection attempts to avoid storms
        if (DateTime.UtcNow - _lastConnectAttemptUtc < ReconnectMinInterval)
            return conn; // Return stale connection (or null) — don't retry yet

        lock (_connectionLock)
        {
            // Double-check after acquiring lock
            if (_disposed) return null;
            conn = _connection;
            if (conn is not null && conn.IsConnected)
                return conn;

            if (DateTime.UtcNow - _lastConnectAttemptUtc < ReconnectMinInterval)
                return conn;

            _lastConnectAttemptUtc = DateTime.UtcNow;

            // Dispose old broken connection before attempting reconnect
            var old = _connection;
            Exception? lastException = null;

            // Retry connection with exponential backoff
            for (var attempt = 1; attempt <= MaxConnectionRetries; attempt++)
            {
                try
                {
                    var options = ConfigurationOptions.Parse(_connectionString);
                    options.AbortOnConnectFail = false;  // Allow startup without Redis
                    options.ConnectTimeout = 3000;       // 3 second connect timeout
                    options.SyncTimeout = 1000;          // 1 second sync timeout
                    options.AsyncTimeout = 1000;         // 1 second async timeout
                    _connection = ConnectionMultiplexer.Connect(options);

                    if (_connection.IsConnected)
                    {
                        _logger.LogInformation("Redis cache connected successfully on attempt {Attempt}", attempt);

                        // Dispose old connection after successful replacement
                        if (old is not null && !ReferenceEquals(old, _connection))
                        {
                            try { old.Dispose(); }
                            catch (Exception) { /* best-effort cleanup */ }
                        }

                        return _connection;
                    }

                    // Connection object created but not connected — dispose and retry
                    _connection.Dispose();
                    _connection = null;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _logger.LogDebug(ex, "Redis connection attempt {Attempt}/{MaxAttempts} failed", attempt, MaxConnectionRetries);
                }

                // Wait before retry (exponential backoff: 500ms, 1s, 2s, ...)
                if (attempt < MaxConnectionRetries)
                {
                    var delay = TimeSpan.FromMilliseconds(RetryBaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
                    Thread.Sleep(delay);
                }
            }

            _logger.LogWarning(lastException, "Redis cache connection failed after {MaxAttempts} attempts — operating in degraded (no-cache) mode", MaxConnectionRetries);
            return null;
        }
    }

    private string BuildKey(string key) => $"{_keyPrefix}:{key}";

    private void LogCacheMetric(string outcome, string keyPrefix)
    {
        var resource = keyPrefix.Split(':').FirstOrDefault() ?? "unknown";
        _logger.LogDebug("CacheMetric outcome={Outcome} resource={Resource}", outcome, resource);
    }
}
