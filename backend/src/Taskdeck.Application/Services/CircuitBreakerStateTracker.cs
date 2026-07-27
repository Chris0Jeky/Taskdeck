using System.Collections.Concurrent;

namespace Taskdeck.Application.Services;

/// <summary>
/// Thread-safe singleton that tracks the state of Polly circuit breakers
/// for external service HTTP clients. The health endpoint reads this to
/// report whether circuits are closed, open, or half-open.
/// </summary>
public sealed class CircuitBreakerStateTracker
{
    private readonly ConcurrentDictionary<string, CircuitBreakerSnapshot> _states = new();
    private readonly ConcurrentDictionary<string, ProviderFailureState> _providerFailures = new();

    /// <summary>
    /// Records a circuit state transition. Called by the Polly <c>onBreak</c>,
    /// <c>onReset</c>, and <c>onHalfOpen</c> delegates.
    /// </summary>
    public void RecordState(string circuitName, CircuitState state, string? lastFailureReason = null)
    {
        _states[circuitName] = new CircuitBreakerSnapshot(
            circuitName,
            state,
            DateTimeOffset.UtcNow,
            lastFailureReason);
    }

    /// <summary>
    /// Returns the current snapshot for every tracked circuit.
    /// </summary>
    public IReadOnlyDictionary<string, CircuitBreakerSnapshot> GetAll()
    {
        return _states;
    }

    /// <summary>
    /// Returns the snapshot for a single circuit, or null if the circuit has
    /// never transitioned (i.e., it has been closed since startup).
    /// </summary>
    public CircuitBreakerSnapshot? Get(string circuitName)
    {
        return _states.TryGetValue(circuitName, out var snapshot) ? snapshot : null;
    }

    /// <summary>
    /// Applies the configured circuit posture to failures that occur after HTTP
    /// response headers. Polly cannot observe body-read, SSE-parse, or idle-timeout
    /// failures when callers use ResponseHeadersRead, so providers report those
    /// failures explicitly through this companion gate.
    /// </summary>
    internal bool TryEnterProviderRequest(
        string circuitName,
        CircuitBreakerSettings settings,
        out CircuitRequestLease lease,
        out string? error)
    {
        while (true)
        {
            if (!_providerFailures.TryGetValue(circuitName, out var state))
            {
                lease = default;
                error = null;
                return true;
            }

            if (state.HalfOpenProbeId is not null)
            {
                lease = default;
                error = $"{circuitName} provider circuit is half-open and its probe is already in progress.";
                return false;
            }

            if (state.OpenUntilUtc is null)
            {
                lease = default;
                error = null;
                return true;
            }

            var now = DateTimeOffset.UtcNow;
            if (state.OpenUntilUtc > now)
            {
                lease = default;
                error = $"{circuitName} provider circuit is open after repeated transport, body, or protocol failures.";
                return false;
            }

            var probeId = Guid.NewGuid();
            var halfOpen = new ProviderFailureState(
                Math.Max(0, settings.FailureThreshold - 1),
                null,
                probeId);
            if (_providerFailures.TryUpdate(circuitName, halfOpen, state))
            {
                RecordState(circuitName, CircuitState.HalfOpen);
                lease = new CircuitRequestLease(probeId);
                error = null;
                return true;
            }
        }
    }

    internal void RecordProviderFailure(
        string circuitName,
        CircuitBreakerSettings settings,
        string reason,
        CircuitRequestLease lease)
    {
        var now = DateTimeOffset.UtcNow;
        while (true)
        {
            if (!_providerFailures.TryGetValue(circuitName, out var existing))
            {
                if (lease.IsHalfOpenProbe)
                    return;

                var initial = new ProviderFailureState(1, null, null);
                if (!_providerFailures.TryAdd(circuitName, initial))
                    continue;
                existing = initial;
            }
            else
            {
                // Ignore stale completions from requests that pre-date a half-open probe,
                // and ignore outcomes from a superseded probe lease.
                if (existing.HalfOpenProbeId is not null &&
                    existing.HalfOpenProbeId != lease.HalfOpenProbeId)
                    return;
                if (lease.HalfOpenProbeId is not null &&
                    existing.HalfOpenProbeId != lease.HalfOpenProbeId)
                    return;
                if (existing.OpenUntilUtc is not null && existing.OpenUntilUtc > now &&
                    lease.HalfOpenProbeId is null)
                    return;

                var failureCount = lease.IsHalfOpenProbe
                    ? settings.FailureThreshold
                    : existing.ConsecutiveFailures + 1;
                var next = new ProviderFailureState(failureCount, null, null);
                if (!_providerFailures.TryUpdate(circuitName, next, existing))
                    continue;
                existing = next;
            }

            if (existing.ConsecutiveFailures < settings.FailureThreshold)
                return;

            var opened = new ProviderFailureState(
                existing.ConsecutiveFailures,
                now.AddSeconds(settings.BreakDurationSeconds),
                null);
            if (_providerFailures.TryUpdate(circuitName, opened, existing))
            {
                RecordState(circuitName, CircuitState.Open, reason);
                return;
            }
        }
    }

    internal void RecordProviderSuccess(string circuitName, CircuitRequestLease lease)
    {
        while (_providerFailures.TryGetValue(circuitName, out var existing))
        {
            if (lease.HalfOpenProbeId is not null && existing.HalfOpenProbeId != lease.HalfOpenProbeId)
                return;
            if (lease.HalfOpenProbeId is null && existing.HalfOpenProbeId is not null)
                return;
            if (((ICollection<KeyValuePair<string, ProviderFailureState>>)_providerFailures).Remove(
                    new KeyValuePair<string, ProviderFailureState>(circuitName, existing)))
            {
                RecordState(circuitName, CircuitState.Closed);
                return;
            }
        }
    }

    internal void AbandonProviderRequest(string circuitName, CircuitRequestLease lease)
    {
        if (!lease.IsHalfOpenProbe)
            return;

        while (_providerFailures.TryGetValue(circuitName, out var existing))
        {
            if (existing.HalfOpenProbeId != lease.HalfOpenProbeId)
                return;

            // Release the exclusive probe immediately while retaining the open posture.
            // The next caller can acquire a fresh half-open lease without waiting through
            // another break duration after cancellation or iterator disposal.
            var released = new ProviderFailureState(
                existing.ConsecutiveFailures,
                DateTimeOffset.UtcNow,
                null);
            if (_providerFailures.TryUpdate(circuitName, released, existing))
            {
                RecordState(circuitName, CircuitState.Open, "Half-open provider probe was abandoned.");
                return;
            }
        }
    }

    private sealed record ProviderFailureState(
        int ConsecutiveFailures,
        DateTimeOffset? OpenUntilUtc,
        Guid? HalfOpenProbeId);
}

internal readonly record struct CircuitRequestLease(Guid? HalfOpenProbeId)
{
    public bool IsHalfOpenProbe => HalfOpenProbeId is not null;
}

public enum CircuitState
{
    Closed,
    Open,
    HalfOpen
}

public record CircuitBreakerSnapshot(
    string CircuitName,
    CircuitState State,
    DateTimeOffset LastTransitionUtc,
    string? LastFailureReason = null);
