using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class AbuseDetectionService : IAbuseDetectionService
{
    private readonly AbuseDetectionSettings _settings;
    private readonly AbuseDetectionState _state;
    private readonly ILlmUsageRecordRepository? _usageRecords;

    public AbuseDetectionService(
        AbuseDetectionSettings settings,
        AbuseDetectionState state,
        ILlmUsageRecordRepository? usageRecords = null)
    {
        _settings = settings;
        _state = state;
        _usageRecords = usageRecords;
    }

    public Task<AbuseSignalResultDto> RecordSignalAsync(
        Guid actorUserId,
        AbuseSignalType signalType,
        string reason,
        CancellationToken ct = default)
    {
        if (!_settings.Enabled)
        {
            return Task.FromResult(new AbuseSignalResultDto(
                actorUserId, signalType, AbuseState.Observe, AbuseState.Observe,
                false, AbuseContainmentAction.None));
        }

        lock (_state.Lock)
        {
            var actor = GetOrCreateActor(actorUserId);
            var previousState = actor.CurrentState;

            var targetState = DetermineTargetState(actor.SignalCount + 1);
            var containment = AbuseActor.DefaultContainmentFor(targetState);
            var stateChanged = actor.RecordSignalAndEscalate(targetState, containment);

            var abuseEvent = new AbuseEvent(
                actorUserId,
                signalType,
                previousState,
                actor.CurrentState,
                actor.ActiveContainment,
                reason);

            _state.Events.Add(abuseEvent);

            return Task.FromResult(new AbuseSignalResultDto(
                actorUserId,
                signalType,
                previousState,
                actor.CurrentState,
                stateChanged,
                actor.ActiveContainment));
        }
    }

    public Task<bool> IsBlockedAsync(Guid actorUserId, CancellationToken ct = default)
    {
        if (!_settings.Enabled)
            return Task.FromResult(false);

        if (_state.Actors.TryGetValue(actorUserId, out var actor))
            return Task.FromResult(actor.IsBlocked);

        return Task.FromResult(false);
    }

    public Task<AbuseActorStatusDto> GetActorStatusAsync(Guid actorUserId, CancellationToken ct = default)
    {
        var actor = _state.Actors.GetValueOrDefault(actorUserId);

        if (actor == null)
        {
            return Task.FromResult(new AbuseActorStatusDto(
                actorUserId,
                AbuseState.Observe,
                AbuseContainmentAction.None,
                SignalCount: 0,
                IsBlocked: false,
                RequiresStricterThrottles: false,
                EscalatedAt: null,
                LastOverrideAt: null,
                LastOverrideByUserId: null));
        }

        return Task.FromResult(new AbuseActorStatusDto(
            actor.UserId,
            actor.CurrentState,
            actor.ActiveContainment,
            actor.SignalCount,
            actor.IsBlocked,
            actor.RequiresStricterThrottles,
            actor.EscalatedAt,
            actor.LastOverrideAt,
            actor.LastOverrideByUserId));
    }

    public Task<Result> OverrideActorStateAsync(
        Guid actorUserId,
        AbuseState newState,
        string reason,
        Guid operatorUserId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return Task.FromResult(Result.Failure(ErrorCodes.ValidationError, "Override reason is required"));

        if (operatorUserId == Guid.Empty)
            return Task.FromResult(Result.Failure(ErrorCodes.ValidationError, "Operator user ID is required"));

        lock (_state.Lock)
        {
            var actor = GetOrCreateActor(actorUserId);
            var previousState = actor.CurrentState;
            var containment = AbuseActor.DefaultContainmentFor(newState);

            actor.OverrideState(newState, containment, operatorUserId);

            var signalType = newState < previousState
                ? AbuseSignalType.ManualOverride
                : AbuseSignalType.ManualEscalation;

            var abuseEvent = new AbuseEvent(
                actorUserId,
                signalType,
                previousState,
                newState,
                containment,
                reason,
                operatorUserId);

            _state.Events.Add(abuseEvent);
        }

        return Task.FromResult(Result.Success());
    }

    public Task<IReadOnlyList<AbuseEventDto>> GetAuditTrailAsync(
        Guid actorUserId,
        int limit = 50,
        CancellationToken ct = default)
    {
        var events = _state.Events
            .Where(e => e.ActorUserId == actorUserId)
            .OrderByDescending(e => e.CreatedAt)
            .Take(limit)
            .Select(e => new AbuseEventDto(
                e.Id,
                e.ActorUserId,
                e.SignalType,
                e.PreviousState,
                e.NewState,
                e.ContainmentAction,
                e.Reason,
                e.OperatorUserId,
                e.CreatedAt))
            .ToList();

        return Task.FromResult<IReadOnlyList<AbuseEventDto>>(events);
    }

    public async Task<bool> EvaluateActorAsync(Guid actorUserId, CancellationToken ct = default)
    {
        if (!_settings.Enabled)
            return false;

        if (_usageRecords == null)
            return false;

        var now = DateTimeOffset.UtcNow;
        var signalsDetected = false;

        // Check anomalous velocity (requests per hour)
        if (_settings.VelocityRequestsPerHourThreshold > 0)
        {
            var hourStart = now.AddHours(-1);
            var requestCount = await _usageRecords.GetRequestCountAsync(
                actorUserId, null, hourStart, now, ct);

            if (requestCount >= _settings.VelocityRequestsPerHourThreshold)
            {
                await RecordSignalAsync(
                    actorUserId,
                    AbuseSignalType.AnomalousVelocity,
                    $"Request velocity {requestCount} requests/hour exceeds threshold {_settings.VelocityRequestsPerHourThreshold}",
                    ct);
                signalsDetected = true;
            }
        }

        // Check anomalous velocity (tokens per hour)
        if (_settings.VelocityTokensPerHourThreshold > 0)
        {
            var hourStart = now.AddHours(-1);
            var tokenCount = await _usageRecords.GetTotalTokensAsync(
                actorUserId, null, hourStart, now, ct);

            if (tokenCount >= _settings.VelocityTokensPerHourThreshold)
            {
                await RecordSignalAsync(
                    actorUserId,
                    AbuseSignalType.AnomalousVelocity,
                    $"Token velocity {tokenCount} tokens/hour exceeds threshold {_settings.VelocityTokensPerHourThreshold}",
                    ct);
                signalsDetected = true;
            }
        }

        return signalsDetected;
    }

    private AbuseActor GetOrCreateActor(Guid userId)
    {
        return _state.Actors.GetOrAdd(userId, id => new AbuseActor(id));
    }

    private AbuseState DetermineTargetState(int signalCount)
    {
        if (signalCount >= _settings.BlockedSignalThreshold)
            return AbuseState.Blocked;

        if (signalCount >= _settings.RestrictedSignalThreshold)
            return AbuseState.Restricted;

        if (signalCount >= _settings.SuspiciousSignalThreshold)
            return AbuseState.Suspicious;

        return AbuseState.Observe;
    }
}
