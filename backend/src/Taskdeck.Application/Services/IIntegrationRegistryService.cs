using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

public interface IIntegrationRegistryService
{
    Task<Result<IntegrationConnectorDto>> RegisterConnectorAsync(
        Guid userId,
        CreateIntegrationConnectorDto dto,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<IntegrationConnectorDto>>> ListConnectorsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<Result<IntegrationConnectorDetailDto>> GetConnectorAsync(
        Guid connectorId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<Result<IntegrationConnectorDto>> UpdateConnectorAsync(
        Guid connectorId,
        Guid userId,
        UpdateIntegrationConnectorDto dto,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteConnectorAsync(
        Guid connectorId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<Result<IntegrationConnectorDto>> EnableConnectorAsync(
        Guid connectorId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<Result<IntegrationConnectorDto>> DisableConnectorAsync(
        Guid connectorId,
        Guid userId,
        CancellationToken cancellationToken = default);
}
