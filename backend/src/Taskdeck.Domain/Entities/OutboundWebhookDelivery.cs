using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

public class OutboundWebhookDelivery : Entity
{
    private const int MaxEventTypeLength = 120;
    private const int MaxErrorMessageLength = 1000;

    public Guid SubscriptionId { get; private set; }
    public Guid BoardId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public WebhookDeliveryStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public int? LastResponseStatusCode { get; private set; }
    public string? LastErrorMessage { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }

    public OutboundWebhookSubscription Subscription { get; private set; } = null!;

    private OutboundWebhookDelivery() : base()
    {
    }

    public OutboundWebhookDelivery(
        Guid subscriptionId,
        Guid boardId,
        string eventType,
        string payload)
        : this(Guid.NewGuid(), subscriptionId, boardId, eventType, payload)
    {
    }

    public OutboundWebhookDelivery(
        Guid id,
        Guid subscriptionId,
        Guid boardId,
        string eventType,
        string payload)
        : base(id)
    {
        if (id == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Delivery ID cannot be empty.");

        if (subscriptionId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Subscription ID cannot be empty.");

        if (boardId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Board ID cannot be empty.");

        if (string.IsNullOrWhiteSpace(eventType))
            throw new DomainException(ErrorCodes.ValidationError, "Event type is required.");

        if (string.IsNullOrWhiteSpace(payload))
            throw new DomainException(ErrorCodes.ValidationError, "Payload is required.");

        var normalizedEventType = eventType.Trim().ToLowerInvariant();
        if (normalizedEventType.Length > MaxEventTypeLength)
            throw new DomainException(
                ErrorCodes.ValidationError,
                $"Event type must be {MaxEventTypeLength} characters or fewer.");

        SubscriptionId = subscriptionId;
        BoardId = boardId;
        EventType = normalizedEventType;
        Payload = payload;
        Status = WebhookDeliveryStatus.Pending;
        NextAttemptAt = DateTimeOffset.UtcNow;
    }

    public void MarkProcessing()
    {
        if (Status != WebhookDeliveryStatus.Pending)
            throw new DomainException(ErrorCodes.InvalidOperation, "Only pending deliveries can be marked processing.");

        Status = WebhookDeliveryStatus.Processing;
        LastAttemptAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void MarkDelivered(int? responseStatusCode = null)
    {
        if (Status != WebhookDeliveryStatus.Processing)
            throw new DomainException(ErrorCodes.InvalidOperation, "Only processing deliveries can be marked delivered.");

        AttemptCount += 1;
        Status = WebhookDeliveryStatus.Delivered;
        DeliveredAt = DateTimeOffset.UtcNow;
        LastResponseStatusCode = responseStatusCode;
        LastErrorMessage = null;
        Touch();
    }

    public void ScheduleRetry(string errorMessage, DateTimeOffset nextAttemptAt, int? responseStatusCode = null)
    {
        if (Status != WebhookDeliveryStatus.Processing)
            throw new DomainException(ErrorCodes.InvalidOperation, "Only processing deliveries can be retried.");

        if (string.IsNullOrWhiteSpace(errorMessage))
            throw new DomainException(ErrorCodes.ValidationError, "Retry error message is required.");

        AttemptCount += 1;
        Status = WebhookDeliveryStatus.Pending;
        NextAttemptAt = nextAttemptAt;
        LastResponseStatusCode = responseStatusCode;
        LastErrorMessage = NormalizeErrorMessage(errorMessage);
        Touch();
    }

    public void MarkDeadLetter(string errorMessage, int? responseStatusCode = null)
    {
        if (Status != WebhookDeliveryStatus.Processing)
            throw new DomainException(ErrorCodes.InvalidOperation, "Only processing deliveries can be dead-lettered.");

        if (string.IsNullOrWhiteSpace(errorMessage))
            throw new DomainException(ErrorCodes.ValidationError, "Dead-letter error message is required.");

        AttemptCount += 1;
        Status = WebhookDeliveryStatus.DeadLetter;
        LastResponseStatusCode = responseStatusCode;
        LastErrorMessage = NormalizeErrorMessage(errorMessage);
        Touch();
    }

    public void ReturnToPending(DateTimeOffset nextAttemptAt, string? errorMessage = null, int? responseStatusCode = null)
    {
        if (Status != WebhookDeliveryStatus.Processing)
            throw new DomainException(ErrorCodes.InvalidOperation, "Only processing deliveries can be returned to pending.");

        Status = WebhookDeliveryStatus.Pending;
        NextAttemptAt = nextAttemptAt;
        LastResponseStatusCode = responseStatusCode;
        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            LastErrorMessage = NormalizeErrorMessage(errorMessage);
        }

        Touch();
    }

    private static string NormalizeErrorMessage(string errorMessage)
    {
        var trimmed = errorMessage.Trim();
        if (trimmed.Length <= MaxErrorMessageLength)
        {
            return trimmed;
        }

        return trimmed[..MaxErrorMessageLength];
    }
}
