using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.DTOs;

public sealed record CreateIntegrationConnectorDto(
    string Name,
    ConnectorType ConnectorType,
    ConnectorDirection Direction,
    string? Configuration = null);

public sealed record UpdateIntegrationConnectorDto(
    string? Name = null,
    string? Configuration = null);

public sealed record IntegrationConnectorDto(
    Guid Id,
    string Name,
    ConnectorType ConnectorType,
    ConnectorDirection Direction,
    ConnectorStatus Status,
    string? Configuration,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record IntegrationConnectorDetailDto(
    Guid Id,
    string Name,
    ConnectorType ConnectorType,
    ConnectorDirection Direction,
    ConnectorStatus Status,
    string? Configuration,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ConnectorEventDto> RecentEvents);

public sealed record ConnectorEventDto(
    Guid Id,
    ConnectorEventType EventType,
    string? Payload,
    DateTimeOffset CreatedAt);
