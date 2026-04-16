using Taskdeck.Domain.Connectors;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Connectors;

public sealed record ConnectorProviderSummaryDto(
    string ProviderId,
    string DisplayName,
    ConnectorType ConnectorType,
    ConnectorDirection Direction,
    string Description);

public sealed record ConnectorProviderHealthDto(
    string ProviderId,
    ConnectorHealthStatus Status,
    string Message,
    DateTimeOffset CheckedAt);

public sealed record StoreConnectorCredentialDto(
    ConnectorAuthMethod AuthMethod,
    string Label,
    string Value,
    DateTimeOffset? ExpiresAt = null);
