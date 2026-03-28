using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

public sealed class AgentRunEvent : Entity
{
    private const int MaxEventTypeLength = 100;
    private const int MaxPayloadLength = 16000;

    public Guid RunId { get; private set; }
    public int SequenceNumber { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string Payload { get; private set; } = "{}";
    public DateTimeOffset Timestamp { get; private set; }

    public AgentRun Run { get; private set; } = null!;

    private AgentRunEvent() : base() { } // EF Core

    public AgentRunEvent(
        Guid runId,
        int sequenceNumber,
        string eventType,
        string? payload = null)
        : base()
    {
        if (runId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "RunId cannot be empty");

        if (sequenceNumber < 0)
            throw new DomainException(ErrorCodes.ValidationError, "SequenceNumber cannot be negative");

        if (string.IsNullOrWhiteSpace(eventType))
            throw new DomainException(ErrorCodes.ValidationError, "EventType cannot be empty");

        if (eventType.Length > MaxEventTypeLength)
            throw new DomainException(ErrorCodes.ValidationError, $"EventType cannot exceed {MaxEventTypeLength} characters");

        if (payload is not null && payload.Length > MaxPayloadLength)
            throw new DomainException(ErrorCodes.ValidationError, $"Payload cannot exceed {MaxPayloadLength} characters");

        RunId = runId;
        SequenceNumber = sequenceNumber;
        EventType = eventType;
        Payload = payload ?? "{}";
        Timestamp = DateTimeOffset.UtcNow;
    }
}
