using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IIntegrationConnectorRepository : IRepository<IntegrationConnector>
{
    Task<IReadOnlyList<IntegrationConnector>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IntegrationConnector?> GetByIdForUserAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default);
}
