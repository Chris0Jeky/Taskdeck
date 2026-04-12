using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IMfaCredentialRepository : IRepository<MfaCredential>
{
    Task<MfaCredential?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task DeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
}
