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
/// </summary>
public sealed class RedisBackplaneHealthCheck
{
    private readonly string? _connectionString;
    private readonly ILogger<RedisBackplaneHealthCheck> _logger;

    public RedisBackplaneHealthCheck(
        IConfiguration configuration,
        ILogger<RedisBackplaneHealthCheck> logger)
    {
        _connectionString = configuration["SignalR:Redis:ConnectionString"];
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_connectionString);

    public async Task<RedisHealthStatus> CheckAsync(CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return new RedisHealthStatus("NotConfigured", null);
        }

        try
        {
            // Parse without logging the raw connection string (may contain password).
            var options = ConfigurationOptions.Parse(_connectionString!);
            options.AbortOnConnectFail = true;
            options.ConnectTimeout = 3000;

            using var connection = await ConnectionMultiplexer.ConnectAsync(options);
            var db = connection.GetDatabase();
            var pong = await db.PingAsync();

            _logger.LogDebug("Redis backplane health check: PING responded in {Latency}ms", pong.TotalMilliseconds);

            return new RedisHealthStatus("Healthy", null, pong.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis backplane health check failed");
            return new RedisHealthStatus("Unhealthy", ex.Message);
        }
    }
}

public record RedisHealthStatus(string Status, string? Error, double? LatencyMs = null);
