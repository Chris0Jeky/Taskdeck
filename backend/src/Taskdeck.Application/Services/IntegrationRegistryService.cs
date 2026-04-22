using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class IntegrationRegistryService : IIntegrationRegistryService
{
    private readonly IIntegrationConnectorRepository _connectorRepository;
    private readonly IConnectorEventRepository _eventRepository;
    private readonly IUnitOfWork _unitOfWork;

    public IntegrationRegistryService(
        IIntegrationConnectorRepository connectorRepository,
        IConnectorEventRepository eventRepository,
        IUnitOfWork unitOfWork)
    {
        _connectorRepository = connectorRepository;
        _eventRepository = eventRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<IntegrationConnectorDto>> RegisterConnectorAsync(
        Guid userId,
        CreateIntegrationConnectorDto dto,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var connector = new IntegrationConnector(
                dto.Name,
                dto.ConnectorType,
                dto.Direction,
                userId,
                dto.Configuration);

            await _connectorRepository.AddAsync(connector, cancellationToken);

            // Record a Connected event for auditability
            var connectorEvent = new ConnectorEvent(
                connector.Id,
                ConnectorEventType.Connected,
                $"Connector '{dto.Name}' registered.");
            await _eventRepository.AddAsync(connectorEvent, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(MapToDto(connector));
        }
        catch (DomainException ex)
        {
            return Result.Failure<IntegrationConnectorDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<IReadOnlyList<IntegrationConnectorDto>>> ListConnectorsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var connectors = await _connectorRepository.GetByUserIdAsync(userId, cancellationToken);
        var dtos = connectors.Select(MapToDto).ToList();
        return Result.Success<IReadOnlyList<IntegrationConnectorDto>>(dtos);
    }

    public async Task<Result<IntegrationConnectorDetailDto>> GetConnectorAsync(
        Guid connectorId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var connector = await _connectorRepository.GetByIdForUserAsync(connectorId, userId, cancellationToken);
        if (connector == null)
            return Result.Failure<IntegrationConnectorDetailDto>(ErrorCodes.NotFound, "Connector not found.");

        var events = await _eventRepository.GetRecentByConnectorIdAsync(connectorId, 20, cancellationToken);
        var recentEvents = events.Select(MapToEventDto).ToList();

        return Result.Success(MapToDetailDto(connector, recentEvents));
    }

    public async Task<Result<IntegrationConnectorDto>> UpdateConnectorAsync(
        Guid connectorId,
        Guid userId,
        UpdateIntegrationConnectorDto dto,
        CancellationToken cancellationToken = default)
    {
        var connector = await _connectorRepository.GetByIdForUserAsync(connectorId, userId, cancellationToken);
        if (connector == null)
            return Result.Failure<IntegrationConnectorDto>(ErrorCodes.NotFound, "Connector not found.");

        try
        {
            if (dto.Name != null)
                connector.UpdateName(dto.Name);

            // Only update configuration when explicitly provided in the DTO.
            // A null Configuration means "no change", preserving the existing value.
            if (dto.Configuration != null)
                connector.UpdateConfiguration(dto.Configuration);

            await _connectorRepository.UpdateAsync(connector, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(MapToDto(connector));
        }
        catch (DomainException ex)
        {
            return Result.Failure<IntegrationConnectorDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result> DeleteConnectorAsync(
        Guid connectorId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var connector = await _connectorRepository.GetByIdForUserAsync(connectorId, userId, cancellationToken);
        if (connector == null)
            return Result.Failure(ErrorCodes.NotFound, "Connector not found.");

        await _connectorRepository.DeleteAsync(connector, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result<IntegrationConnectorDto>> EnableConnectorAsync(
        Guid connectorId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var connector = await _connectorRepository.GetByIdForUserAsync(connectorId, userId, cancellationToken);
        if (connector == null)
            return Result.Failure<IntegrationConnectorDto>(ErrorCodes.NotFound, "Connector not found.");

        try
        {
            connector.Enable();
            await _connectorRepository.UpdateAsync(connector, cancellationToken);

            var connectorEvent = new ConnectorEvent(
                connector.Id,
                ConnectorEventType.Connected,
                "Connector enabled.");
            await _eventRepository.AddAsync(connectorEvent, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(MapToDto(connector));
        }
        catch (DomainException ex)
        {
            return Result.Failure<IntegrationConnectorDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<IntegrationConnectorDto>> DisableConnectorAsync(
        Guid connectorId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var connector = await _connectorRepository.GetByIdForUserAsync(connectorId, userId, cancellationToken);
        if (connector == null)
            return Result.Failure<IntegrationConnectorDto>(ErrorCodes.NotFound, "Connector not found.");

        try
        {
            connector.Disable();
            await _connectorRepository.UpdateAsync(connector, cancellationToken);

            var connectorEvent = new ConnectorEvent(
                connector.Id,
                ConnectorEventType.Disconnected,
                "Connector disabled.");
            await _eventRepository.AddAsync(connectorEvent, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(MapToDto(connector));
        }
        catch (DomainException ex)
        {
            return Result.Failure<IntegrationConnectorDto>(ex.ErrorCode, ex.Message);
        }
    }

    private static IntegrationConnectorDto MapToDto(IntegrationConnector connector)
    {
        return new IntegrationConnectorDto(
            connector.Id,
            connector.Name,
            connector.ConnectorType,
            connector.Direction,
            connector.Status,
            connector.Configuration,
            connector.CreatedAt,
            connector.UpdatedAt);
    }

    private static IntegrationConnectorDetailDto MapToDetailDto(
        IntegrationConnector connector,
        IReadOnlyList<ConnectorEventDto> recentEvents)
    {
        return new IntegrationConnectorDetailDto(
            connector.Id,
            connector.Name,
            connector.ConnectorType,
            connector.Direction,
            connector.Status,
            connector.Configuration,
            connector.CreatedAt,
            connector.UpdatedAt,
            recentEvents);
    }

    private static ConnectorEventDto MapToEventDto(ConnectorEvent e)
    {
        return new ConnectorEventDto(e.Id, e.EventType, e.Payload, e.CreatedAt);
    }
}
