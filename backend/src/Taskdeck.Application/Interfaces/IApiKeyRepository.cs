using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IApiKeyRepository : IRepository<ApiKey>
{
    /// <summary>Look up an API key by its SHA-256 hash.</summary>
    Task<ApiKey?> GetByKeyHashAsync(string keyHash, CancellationToken cancellationToken = default);

    /// <summary>List all API keys belonging to a user (including revoked).</summary>
    Task<IEnumerable<ApiKey>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
}
