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

    /// <summary>
    /// Get all credentials belonging to a specific user.
    /// </summary>
    Task<IReadOnlyList<ConnectorCredential>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a single credential by connector ID and user ID (user-scoped single-credential access).
    /// Alias for GetByConnectorIdForUserAsync for explicit naming clarity.
    /// </summary>
    Task<ConnectorCredential?> GetByConnectorIdAndUserIdAsync(
        Guid connectorId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task DeleteByConnectorIdAsync(
        Guid connectorId,
        Guid userId,
        CancellationToken cancellationToken = default);
}
