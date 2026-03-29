using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IExternalLoginRepository : IRepository<ExternalLogin>
{
    Task<ExternalLogin?> GetByProviderAsync(string provider, string providerUserId, CancellationToken cancellationToken = default);
    Task<IEnumerable<ExternalLogin>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
}
