using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.DTOs;

public record AbuseActorStatusDto(
    Guid UserId,
    AbuseState CurrentState,
    AbuseContainmentAction ActiveContainment,
    int SignalCount,
    bool IsBlocked,
    bool RequiresStricterThrottles,
    DateTimeOffset? EscalatedAt,
    DateTimeOffset? LastOverrideAt,
    Guid? LastOverrideByUserId);

public record AbuseEventDto(
    Guid Id,
    Guid ActorUserId,
    AbuseSignalType SignalType,
    AbuseState PreviousState,
    AbuseState NewState,
    AbuseContainmentAction ContainmentAction,
    string Reason,
    Guid? OperatorUserId,
    DateTimeOffset CreatedAt);

public record AbuseOverrideRequestDto(
    Guid ActorUserId,
    AbuseState NewState,
    string Reason);

public record AbuseSignalResultDto(
    Guid ActorUserId,
    AbuseSignalType SignalType,
    AbuseState PreviousState,
    AbuseState NewState,
    bool StateChanged,
    AbuseContainmentAction ContainmentAction);
