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
    private readonly ConcurrentDictionary<string, StreamingFailureState> _streamingFailures = new();

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
    public bool TryEnterStreamingRequest(
        string circuitName,
        CircuitBreakerSettings settings,
        out string? error)
    {
        while (true)
        {
            if (!_streamingFailures.TryGetValue(circuitName, out var state))
            {
                error = null;
                return true;
            }

            if (state.HalfOpenProbeInFlight)
            {
                error = $"{circuitName} streaming circuit is half-open and its probe is already in progress.";
                return false;
            }

            if (state.OpenUntilUtc is null)
            {
                error = null;
                return true;
            }

            var now = DateTimeOffset.UtcNow;
            if (state.OpenUntilUtc > now)
            {
                error = $"{circuitName} streaming circuit is open after repeated response-body failures.";
                return false;
            }

            var halfOpen = new StreamingFailureState(
                Math.Max(0, settings.FailureThreshold - 1),
                null,
                HalfOpenProbeInFlight: true);
            if (_streamingFailures.TryUpdate(circuitName, halfOpen, state))
            {
                RecordState(circuitName, CircuitState.HalfOpen);
                error = null;
                return true;
            }
        }
    }

    public void RecordStreamingFailure(
        string circuitName,
        CircuitBreakerSettings settings,
        string reason)
    {
        var now = DateTimeOffset.UtcNow;
        var next = _streamingFailures.AddOrUpdate(
            circuitName,
            _ => new StreamingFailureState(1, null, HalfOpenProbeInFlight: false),
            (_, existing) => existing.OpenUntilUtc is not null && existing.OpenUntilUtc > now
                ? existing
                : new StreamingFailureState(existing.ConsecutiveFailures + 1, null, HalfOpenProbeInFlight: false));

        if (next.ConsecutiveFailures < settings.FailureThreshold && next.OpenUntilUtc is null)
            return;

        var opened = new StreamingFailureState(
            next.ConsecutiveFailures,
            now.AddSeconds(settings.BreakDurationSeconds),
            HalfOpenProbeInFlight: false);
        _streamingFailures[circuitName] = opened;
        RecordState(circuitName, CircuitState.Open, reason);
    }

    public void RecordStreamingSuccess(string circuitName)
    {
        if (_streamingFailures.TryRemove(circuitName, out _))
            RecordState(circuitName, CircuitState.Closed);
    }

    private sealed record StreamingFailureState(
        int ConsecutiveFailures,
        DateTimeOffset? OpenUntilUtc,
        bool HalfOpenProbeInFlight);
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
