using StackExchange.Redis;

namespace Taskdeck.Api.Health;

/// <summary>
/// Checks Redis connectivity for the SignalR backplane.
/// Returns one of three states:
/// <list type="bullet">
///   <item><c>NotConfigured</c> — no Redis connection string, in-memory transport is active</item>
///   <item><c>Healthy</c> — Redis is configured and responding to PING</item>
///   <item><c>Unhealthy</c> — Redis is configured but unreachable</item>
/// </list>
///
/// Registered as a singleton. Holds a single long-lived <see cref="IConnectionMultiplexer"/>
/// to avoid creating a new TCP connection per health probe.
/// </summary>
public sealed class RedisBackplaneHealthCheck : IDisposable
{
    private readonly string? _connectionString;
    private readonly ILogger<RedisBackplaneHealthCheck> _logger;

    /// <summary>
    /// Lazily-initialized Redis connection, shared across all health probes.
    /// ConnectionMultiplexer is designed to be a long-lived singleton that
    /// handles reconnection automatically.
    /// </summary>
    private readonly Lazy<Task<IConnectionMultiplexer>>? _lazyConnection;

    /// <summary>
    /// Thread-safe cached health result. Both status and timestamp are packed
    /// into a single immutable record so reads/writes are atomic via volatile.
    /// Refreshed at most once per <see cref="CacheDuration"/>.
    /// </summary>
    private volatile CacheEntry? _cache;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private record CacheEntry(RedisHealthStatus Status, DateTimeOffset Timestamp);

    public RedisBackplaneHealthCheck(
        IConfiguration configuration,
        ILogger<RedisBackplaneHealthCheck> logger)
    {
        _connectionString = configuration[Extensions.SignalRRegistration.RedisConnectionStringKey];
        _logger = logger;

        if (IsConfigured)
        {
            _lazyConnection = new Lazy<Task<IConnectionMultiplexer>>(async () =>
            {
                var options = ConfigurationOptions.Parse(_connectionString!);
                // Let the multiplexer handle reconnects automatically.
                options.AbortOnConnectFail = false;
                return await ConnectionMultiplexer.ConnectAsync(options);
            });
        }
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_connectionString);

    public async Task<RedisHealthStatus> CheckAsync(CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return new RedisHealthStatus("NotConfigured", null);
        }

        // Return cached result when still fresh to avoid PING on every health
        // probe (called by load balancers, k8s, etc.).
        var snapshot = _cache;
        if (snapshot is not null && DateTimeOffset.UtcNow - snapshot.Timestamp < CacheDuration)
        {
            return snapshot.Status;
        }

        try
        {
            var connection = await _lazyConnection!.Value;

            if (!connection.IsConnected)
            {
                _logger.LogWarning("Redis backplane health check: connection is not connected");
                var unhealthy = new RedisHealthStatus("Unhealthy", "Redis connection is not connected.");
                _cache = new CacheEntry(unhealthy, DateTimeOffset.UtcNow);
                return unhealthy;
            }

            var db = connection.GetDatabase();
            var pong = await db.PingAsync();

            _logger.LogDebug("Redis backplane health check: PING responded in {Latency}ms", pong.TotalMilliseconds);

            var result = new RedisHealthStatus("Healthy", null, pong.TotalMilliseconds);
            _cache = new CacheEntry(result, DateTimeOffset.UtcNow);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis backplane health check failed");
            var result = new RedisHealthStatus("Unhealthy", ex.Message);
            _cache = new CacheEntry(result, DateTimeOffset.UtcNow);
            return result;
        }
    }

    public void Dispose()
    {
        if (_lazyConnection is { IsValueCreated: true })
        {
            try
            {
                _lazyConnection.Value.GetAwaiter().GetResult().Dispose();
            }
            catch
            {
                // Best-effort disposal during shutdown.
            }
        }
    }
}

/// <summary>
/// Health status result for the Redis backplane connectivity check.
/// Serialized as JSON in the <c>/health/ready</c> endpoint response.
/// </summary>
public record RedisHealthStatus(string Status, string? Error, double? LatencyMs = null);
