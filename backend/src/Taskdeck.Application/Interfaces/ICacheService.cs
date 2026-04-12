namespace Taskdeck.Application.Interfaces;

/// <summary>
/// Generic cache abstraction for cache-aside pattern.
/// Implementations must degrade safely: cache failures never throw exceptions
/// to callers and do not affect data correctness.
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Attempts to retrieve a cached value. Returns null on miss or error.
    /// </summary>
    /// <typeparam name="T">The cached value type (must be JSON-serializable).</typeparam>
    /// <param name="key">The cache key (will be prefixed automatically).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached value, or null if not found or on error.</returns>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Stores a value in the cache with the specified TTL.
    /// Silently swallows errors — caller is never affected by cache write failures.
    /// </summary>
    /// <typeparam name="T">The value type (must be JSON-serializable).</typeparam>
    /// <param name="key">The cache key (will be prefixed automatically).</param>
    /// <param name="value">The value to cache.</param>
    /// <param name="ttl">Time-to-live for the cached entry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Removes a cached entry. Silently swallows errors.
    /// </summary>
    /// <param name="key">The cache key to invalidate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes all cached entries matching the specified prefix pattern.
    /// Used for bulk invalidation (e.g., all board list caches for a user).
    /// Silently swallows errors.
    /// </summary>
    /// <param name="keyPrefix">The key prefix to match for removal.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RemoveByPrefixAsync(string keyPrefix, CancellationToken cancellationToken = default);
}
