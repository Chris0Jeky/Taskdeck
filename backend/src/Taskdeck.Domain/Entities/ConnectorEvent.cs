using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

public class ConnectorEvent : Entity
{
    private const int MaxPayloadLength = 1000;

    public Guid ConnectorId { get; private set; }
    public ConnectorEventType EventType { get; private set; }
    public string? Payload { get; private set; }

    private ConnectorEvent() : base() { }

    public ConnectorEvent(
        Guid connectorId,
        ConnectorEventType eventType,
        string? payload = null)
        : base()
    {
        if (connectorId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Connector ID cannot be empty.");

        ConnectorId = connectorId;
        EventType = eventType;
        SetPayload(payload);
    }

    private void SetPayload(string? payload)
    {
        if (payload != null && payload.Length > MaxPayloadLength)
        {
            // Truncate payload to max length for auditability without unbounded storage
            Payload = payload[..MaxPayloadLength];
        }
        else
        {
            Payload = payload;
        }
    }
}
