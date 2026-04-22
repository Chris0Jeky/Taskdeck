using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Connectors;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Connectors;

public sealed class ConnectorCredentialService : IConnectorCredentialService
{
    private readonly IConnectorCredentialRepository _credentialRepository;
    private readonly IIntegrationConnectorRepository _connectorRepository;
    private readonly ICredentialEncryptionService _encryptionService;
    private readonly IUnitOfWork _unitOfWork;

    public ConnectorCredentialService(
        IConnectorCredentialRepository credentialRepository,
        IIntegrationConnectorRepository connectorRepository,
        ICredentialEncryptionService encryptionService,
        IUnitOfWork unitOfWork)
    {
        _credentialRepository = credentialRepository;
        _connectorRepository = connectorRepository;
        _encryptionService = encryptionService;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ConnectorCredentialDto>> StoreCredentialAsync(
        Guid connectorId,
        Guid userId,
        ConnectorAuthMethod authMethod,
        string label,
        string plaintextValue,
        DateTimeOffset? expiresAt = null,
        CancellationToken cancellationToken = default)
    {
        // Verify connector ownership
        var connector = await _connectorRepository.GetByIdForUserAsync(connectorId, userId, cancellationToken);
        if (connector == null)
            return Result.Failure<ConnectorCredentialDto>(ErrorCodes.NotFound, "Connector not found.");

        // Remove any existing credential for this connector
        await _credentialRepository.DeleteByConnectorIdAsync(connectorId, userId, cancellationToken);

        try
        {
            var encryptedValue = _encryptionService.Encrypt(plaintextValue);
            var credential = new ConnectorCredential(
                connectorId,
                userId,
                authMethod,
                label,
                encryptedValue,
                expiresAt);

            await _credentialRepository.AddAsync(credential, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(MapToDto(credential));
        }
        catch (DomainException ex)
        {
            return Result.Failure<ConnectorCredentialDto>(ex.ErrorCode, ex.Message);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return Result.Failure<ConnectorCredentialDto>(
                ErrorCodes.UnexpectedError,
                "Failed to encrypt credential. The encryption key may be invalid.");
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            throw; // Propagate cancellation
        }
        catch (Exception)
        {
            return Result.Failure<ConnectorCredentialDto>(
                ErrorCodes.UnexpectedError,
                "An unexpected error occurred while storing the credential.");
        }
    }

    public async Task<Result<ConnectorCredentialDto>> GetCredentialAsync(
        Guid connectorId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var credential = await _credentialRepository.GetByConnectorIdForUserAsync(
            connectorId, userId, cancellationToken);

        if (credential == null)
            return Result.Failure<ConnectorCredentialDto>(ErrorCodes.NotFound, "Credential not found.");

        return Result.Success(MapToDto(credential));
    }

    public async Task<Result> DeleteCredentialAsync(
        Guid connectorId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var credential = await _credentialRepository.GetByConnectorIdForUserAsync(
            connectorId, userId, cancellationToken);

        if (credential == null)
            return Result.Failure(ErrorCodes.NotFound, "Credential not found.");

        await _credentialRepository.DeleteAsync(credential, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private static ConnectorCredentialDto MapToDto(ConnectorCredential credential)
    {
        return new ConnectorCredentialDto(
            credential.Id,
            credential.ConnectorId,
            credential.AuthMethod,
            credential.Label,
            HasCredential: !string.IsNullOrEmpty(credential.EncryptedValue),
            credential.RotatedAt,
            credential.ExpiresAt,
            credential.IsExpired,
            credential.CreatedAt);
    }
}
