using System.ComponentModel.DataAnnotations;

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
    [Required(AllowEmptyStrings = false)]
    [RegularExpression("^(Redis|InMemory|None)$", ErrorMessage = "Cache Provider must be 'Redis', 'InMemory', or 'None'.")]
    public string Provider { get; set; } = "InMemory";

    /// <summary>
    /// Redis connection string. Only used when Provider is "Redis".
    /// </summary>
    public string? RedisConnectionString { get; set; }

    /// <summary>
    /// Global key prefix to avoid collisions in shared Redis instances.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string KeyPrefix { get; set; } = "td";

    /// <summary>
    /// Default TTL in seconds for board list cache entries.
    /// </summary>
    [Range(1, 86400, ErrorMessage = "BoardListTtlSeconds must be between 1 and 86400 (1 day).")]
    public int BoardListTtlSeconds { get; set; } = 60;
}
