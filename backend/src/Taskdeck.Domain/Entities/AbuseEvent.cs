using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// Immutable audit record for abuse detection events and state transitions.
/// Every automated or manual containment action produces an AbuseEvent.
/// </summary>
public class AbuseEvent : Entity
{
    public Guid ActorUserId { get; private set; }
    public AbuseSignalType SignalType { get; private set; }
    public AbuseState PreviousState { get; private set; }
    public AbuseState NewState { get; private set; }
    public AbuseContainmentAction ContainmentAction { get; private set; }
    public string Reason { get; private set; } = string.Empty;

    /// <summary>Optional operator user ID when the event is a manual override.</summary>
    public Guid? OperatorUserId { get; private set; }

    private AbuseEvent() : base() { }

    public AbuseEvent(
        Guid actorUserId,
        AbuseSignalType signalType,
        AbuseState previousState,
        AbuseState newState,
        AbuseContainmentAction containmentAction,
        string reason,
        Guid? operatorUserId = null)
        : base()
    {
        if (actorUserId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Actor user ID cannot be empty");

        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException(ErrorCodes.ValidationError, "Abuse event reason cannot be empty");

        ActorUserId = actorUserId;
        SignalType = signalType;
        PreviousState = previousState;
        NewState = newState;
        ContainmentAction = containmentAction;
        Reason = reason;
        OperatorUserId = operatorUserId;
    }
}
