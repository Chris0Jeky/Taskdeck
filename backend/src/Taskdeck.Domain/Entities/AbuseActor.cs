using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// Tracks the current abuse state for a managed-key user.
/// State transitions are deterministic and follow: Observe → Suspicious → Restricted → Blocked.
/// All transitions are reversible via operator override.
/// </summary>
public class AbuseActor : Entity
{
    public Guid UserId { get; private set; }
    public AbuseState CurrentState { get; private set; }
    public AbuseContainmentAction ActiveContainment { get; private set; }

    /// <summary>Count of abuse signals recorded in the current evaluation window.</summary>
    public int SignalCount { get; private set; }

    /// <summary>When the actor last transitioned to a non-Observe state.</summary>
    public DateTimeOffset? EscalatedAt { get; private set; }

    /// <summary>When the actor was last de-escalated or cleared by an operator.</summary>
    public DateTimeOffset? LastOverrideAt { get; private set; }

    /// <summary>Operator user ID from the last manual override, if any.</summary>
    public Guid? LastOverrideByUserId { get; private set; }

    private AbuseActor() : base() { }

    public AbuseActor(Guid userId) : base()
    {
        if (userId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "User ID cannot be empty");

        UserId = userId;
        CurrentState = AbuseState.Observe;
        ActiveContainment = AbuseContainmentAction.None;
        SignalCount = 0;
    }

    /// <summary>
    /// Records an abuse signal and escalates state if thresholds are met.
    /// Returns true if the state actually changed.
    /// </summary>
    public bool RecordSignalAndEscalate(AbuseState targetState, AbuseContainmentAction containment)
    {
        SignalCount++;
        Touch();

        if (targetState <= CurrentState)
            return false;

        CurrentState = targetState;
        ActiveContainment = containment;
        EscalatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    /// <summary>
    /// Operator override: set state to any level (including de-escalation).
    /// </summary>
    public void OverrideState(AbuseState newState, AbuseContainmentAction containment, Guid operatorUserId)
    {
        if (operatorUserId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Operator user ID cannot be empty");

        CurrentState = newState;
        ActiveContainment = containment;
        LastOverrideAt = DateTimeOffset.UtcNow;
        LastOverrideByUserId = operatorUserId;

        if (newState == AbuseState.Observe)
        {
            SignalCount = 0;
            EscalatedAt = null;
        }

        Touch();
    }

    /// <summary>
    /// Returns true if the actor is in a state that should block LLM provider calls.
    /// </summary>
    public bool IsBlocked => CurrentState >= AbuseState.Restricted;

    /// <summary>
    /// Returns true if the actor requires stricter throttling.
    /// </summary>
    public bool RequiresStricterThrottles => CurrentState >= AbuseState.Suspicious;

    /// <summary>
    /// Maps an AbuseState to its default containment action.
    /// </summary>
    public static AbuseContainmentAction DefaultContainmentFor(AbuseState state) => state switch
    {
        AbuseState.Observe => AbuseContainmentAction.None,
        AbuseState.Suspicious => AbuseContainmentAction.StricterThrottles,
        AbuseState.Restricted => AbuseContainmentAction.ProviderCallsDisabled,
        AbuseState.Blocked => AbuseContainmentAction.MandatoryManualReview,
        _ => AbuseContainmentAction.None
    };
}
