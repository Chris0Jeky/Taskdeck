using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IOAuthAuthCodeRepository : IRepository<OAuthAuthCode>
{
    /// <summary>
    /// Finds an auth code by its code string. Returns null if not found.
    /// </summary>
    Task<OAuthAuthCode?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all auth codes that have expired before the specified cutoff time.
    /// Returns the number of codes removed.
    /// </summary>
    Task<int> DeleteExpiredAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);
}
