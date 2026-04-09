using Taskdeck.Application.Interfaces;

namespace Taskdeck.Infrastructure.Services;

/// <summary>
/// No-op cache implementation used when caching is explicitly disabled via configuration.
/// All operations return immediately with no side effects.
/// </summary>
public sealed class NoOpCacheService : ICacheService
{
    public static readonly NoOpCacheService Instance = new();

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
        => Task.FromResult<T?>(null);

    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default) where T : class
        => Task.CompletedTask;

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task RemoveByPrefixAsync(string keyPrefix, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
