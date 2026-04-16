using Taskdeck.Domain.Common;
using Taskdeck.Domain.Connectors;

namespace Taskdeck.Application.Connectors;

/// <summary>
/// CRUD for connector credentials with encryption at rest.
/// The service encrypts plaintext before storage and decrypts on retrieval.
/// Plaintext secrets never touch the persistence layer.
/// </summary>
public interface IConnectorCredentialService
{
    /// <summary>
    /// Store a new credential for the given connector.
    /// The plaintext value is encrypted before persistence.
    /// </summary>
    Task<Result<ConnectorCredentialDto>> StoreCredentialAsync(
        Guid connectorId,
        Guid userId,
        ConnectorAuthMethod authMethod,
        string label,
        string plaintextValue,
        DateTimeOffset? expiresAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the credential metadata for a connector (does NOT return the decrypted value).
    /// </summary>
    Task<Result<ConnectorCredentialDto>> GetCredentialAsync(
        Guid connectorId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove the credential for a connector.
    /// </summary>
    Task<Result> DeleteCredentialAsync(
        Guid connectorId,
        Guid userId,
        CancellationToken cancellationToken = default);
}

public sealed record ConnectorCredentialDto(
    Guid Id,
    Guid ConnectorId,
    ConnectorAuthMethod AuthMethod,
    string Label,
    bool HasCredential,
    DateTimeOffset? RotatedAt,
    DateTimeOffset? ExpiresAt,
    bool IsExpired,
    DateTimeOffset CreatedAt);
