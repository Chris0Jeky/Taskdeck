using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IConnectorEventRepository : IRepository<ConnectorEvent>
{
    Task<IReadOnlyList<ConnectorEvent>> GetRecentByConnectorIdAsync(
        Guid connectorId,
        int limit = 20,
        CancellationToken cancellationToken = default);
}
