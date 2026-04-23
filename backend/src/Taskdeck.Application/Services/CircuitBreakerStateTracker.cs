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
