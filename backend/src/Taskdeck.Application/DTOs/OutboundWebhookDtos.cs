using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.DTOs;

public sealed record CreateOutboundWebhookSubscriptionDto(
    string EndpointUrl,
    List<string>? EventFilters = null);

public sealed record OutboundWebhookSubscriptionDto(
    Guid Id,
    Guid BoardId,
    string EndpointUrl,
    List<string> EventFilters,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastTriggeredAt);

public sealed record OutboundWebhookSubscriptionSecretDto(
    OutboundWebhookSubscriptionDto Subscription,
    string SigningSecret);

public sealed record OutboundWebhookDeliveryDto(
    Guid Id,
    Guid SubscriptionId,
    string EventType,
    WebhookDeliveryStatus Status,
    int AttemptCount,
    DateTimeOffset NextAttemptAt,
    DateTimeOffset? LastAttemptAt,
    int? LastResponseStatusCode,
    string? LastErrorMessage,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset CreatedAt);
