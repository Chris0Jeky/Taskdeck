using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IConnectorCredentialRepository : IRepository<ConnectorCredential>
{
    Task<IReadOnlyList<ConnectorCredential>> GetByConnectorIdAsync(
        Guid connectorId,
        CancellationToken cancellationToken = default);

    Task<ConnectorCredential?> GetByConnectorIdForUserAsync(
        Guid connectorId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task DeleteByConnectorIdAsync(
        Guid connectorId,
        Guid userId,
        CancellationToken cancellationToken = default);
}
