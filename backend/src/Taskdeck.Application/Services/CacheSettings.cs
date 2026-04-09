namespace Taskdeck.Application.Services;

/// <summary>
/// Configuration for the distributed caching layer.
/// Bound from appsettings.json "Cache" section.
/// </summary>
public sealed class CacheSettings
{
    /// <summary>
    /// Cache provider: "Redis", "InMemory", or "None".
    /// Defaults to "InMemory" for local-first usage.
    /// </summary>
    public string Provider { get; set; } = "InMemory";

    /// <summary>
    /// Redis connection string. Only used when Provider is "Redis".
    /// </summary>
    public string? RedisConnectionString { get; set; }

    /// <summary>
    /// Global key prefix to avoid collisions in shared Redis instances.
    /// </summary>
    public string KeyPrefix { get; set; } = "td";

    /// <summary>
    /// Default TTL in seconds for board list cache entries.
    /// </summary>
    public int BoardListTtlSeconds { get; set; } = 60;

    /// <summary>
    /// Default TTL in seconds for board detail cache entries.
    /// </summary>
    public int BoardDetailTtlSeconds { get; set; } = 120;
}
