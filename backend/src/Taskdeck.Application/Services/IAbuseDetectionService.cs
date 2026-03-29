using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Services;

public interface IAbuseDetectionService
{
    /// <summary>
    /// Records an abuse signal for the given actor and evaluates whether state escalation is needed.
    /// Returns the signal result including whether the state changed.
    /// </summary>
    Task<AbuseSignalResultDto> RecordSignalAsync(
        Guid actorUserId,
        AbuseSignalType signalType,
        string reason,
        CancellationToken ct = default);

    /// <summary>
    /// Checks whether the given actor is currently blocked from LLM provider calls.
    /// </summary>
    Task<bool> IsBlockedAsync(Guid actorUserId, CancellationToken ct = default);

    /// <summary>
    /// Returns the current abuse status for the given actor.
    /// </summary>
    Task<AbuseActorStatusDto> GetActorStatusAsync(Guid actorUserId, CancellationToken ct = default);

    /// <summary>
    /// Operator override: set actor to any abuse state with an audit trail.
    /// </summary>
    Task<Result> OverrideActorStateAsync(
        Guid actorUserId,
        AbuseState newState,
        string reason,
        Guid operatorUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the abuse event audit trail for the given actor.
    /// </summary>
    Task<IReadOnlyList<AbuseEventDto>> GetAuditTrailAsync(
        Guid actorUserId,
        int limit = 50,
        CancellationToken ct = default);

    /// <summary>
    /// Evaluates the actor's recent LLM usage against abuse thresholds
    /// and records signals for any detected anomalies.
    /// Returns true if any new signals were detected.
    /// </summary>
    Task<bool> EvaluateActorAsync(Guid actorUserId, CancellationToken ct = default);
}
