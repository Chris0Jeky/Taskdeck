using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Taskdeck.Application.Interfaces;

namespace Taskdeck.Infrastructure.Services;

/// <summary>
/// Redis-backed cache implementation for production/multi-instance deployments.
/// All operations degrade safely on connection failure — no exceptions propagated to callers.
/// Uses StackExchange.Redis with lazy connection multiplexer.
/// </summary>
public sealed class RedisCacheService : ICacheService, IDisposable
{
    private readonly Lazy<ConnectionMultiplexer?> _lazyConnection;
    private readonly ILogger<RedisCacheService> _logger;
    private readonly string _keyPrefix;
    private bool _disposed;

    public RedisCacheService(string connectionString, ILogger<RedisCacheService> logger, string keyPrefix = "td")
    {
        _logger = logger;
        _keyPrefix = keyPrefix;

        // Lazy initialization: connection is established on first cache access,
        // not at startup. This prevents startup failures when Redis is unavailable.
        _lazyConnection = new Lazy<ConnectionMultiplexer?>(() =>
        {
            try
            {
                var options = ConfigurationOptions.Parse(connectionString);
                options.AbortOnConnectFail = false;  // Allow startup without Redis
                options.ConnectTimeout = 3000;       // 3 second connect timeout
                options.SyncTimeout = 1000;          // 1 second sync timeout
                options.AsyncTimeout = 1000;         // 1 second async timeout
                var connection = ConnectionMultiplexer.Connect(options);
                _logger.LogInformation("Redis cache connected successfully");
                return connection;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis cache connection failed — operating in degraded (no-cache) mode");
                return null;
            }
        });
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

            var result = JsonSerializer.Deserialize<T>(value!);
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
        var connection = _lazyConnection.Value;
        if (connection is null) return;

        try
        {
            // Use SCAN to find keys by prefix — safe for production (non-blocking).
            // Note: This requires access to a server endpoint.
            var endpoints = connection.GetEndPoints();
            foreach (var endpoint in endpoints)
            {
                var server = connection.GetServer(endpoint);
                var keys = server.Keys(pattern: $"{fullPrefix}*").ToArray();
                if (keys.Length > 0)
                {
                    var db = connection.GetDatabase();
                    await db.KeyDeleteAsync(keys);
                    _logger.LogDebug("Cache removed {Count} keys with prefix {CacheKeyPrefix}", keys.Length, fullPrefix);
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

        if (_lazyConnection.IsValueCreated)
        {
            _lazyConnection.Value?.Dispose();
        }
    }

    private IDatabase? GetDatabase()
    {
        try
        {
            var connection = _lazyConnection.Value;
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

    private string BuildKey(string key) => $"{_keyPrefix}:{key}";

    private void LogCacheMetric(string outcome, string keyPrefix)
    {
        var resource = keyPrefix.Split(':').FirstOrDefault() ?? "unknown";
        _logger.LogInformation("CacheMetric outcome={Outcome} resource={Resource}", outcome, resource);
    }
}
