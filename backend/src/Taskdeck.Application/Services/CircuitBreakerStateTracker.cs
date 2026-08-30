using System.Collections.Concurrent;

namespace Taskdeck.Application.Services;

/// <summary>
/// Thread-safe singleton that tracks Polly circuit state and the companion
/// circuit used for failures that occur after response headers are received.
/// </summary>
public sealed class CircuitBreakerStateTracker
{
    private readonly ConcurrentDictionary<string, CircuitBreakerSnapshot> _pollyStates = new();
    private readonly ConcurrentDictionary<string, ProviderFailureState> _providerFailures = new();
    private readonly TimeProvider _timeProvider;

    public CircuitBreakerStateTracker()
        : this(TimeProvider.System)
    {
    }

    internal CircuitBreakerStateTracker(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>
    /// Records a Polly circuit transition. Companion-provider transitions are
    /// deliberately tracked in a separate lane so neither circuit can mask the other.
    /// </summary>
    public void RecordState(string circuitName, CircuitState state, string? lastFailureReason = null)
    {
        _pollyStates[circuitName] = CreateSnapshot(circuitName, state, lastFailureReason);
    }

    /// <summary>
    /// Returns the most restrictive current snapshot for every tracked circuit.
    /// </summary>
    public IReadOnlyDictionary<string, CircuitBreakerSnapshot> GetAll()
    {
        var names = new HashSet<string>(_pollyStates.Keys, StringComparer.Ordinal);
        names.UnionWith(_providerFailures.Keys);

        var result = new Dictionary<string, CircuitBreakerSnapshot>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            var snapshot = Get(name);
            if (snapshot is not null)
                result[name] = snapshot;
        }

        return result;
    }

    /// <summary>
    /// Returns the most restrictive Polly or companion snapshot for one circuit.
    /// </summary>
    public CircuitBreakerSnapshot? Get(string circuitName)
    {
        _pollyStates.TryGetValue(circuitName, out var polly);
        var companion = _providerFailures.TryGetValue(circuitName, out var state)
            ? state.PublicSnapshot
            : null;
        return SelectMostRestrictive(polly, companion);
    }

    /// <summary>
    /// Applies the configured circuit posture to failures that occur after HTTP
    /// response headers. Every admitted request receives a generation-bearing
    /// lease so outcomes from an older generation cannot mutate newer state.
    /// </summary>
    internal bool TryEnterProviderRequest(
        string circuitName,
        CircuitBreakerSettings settings,
        out CircuitRequestLease lease,
        out string? error)
    {
        while (true)
        {
            var state = _providerFailures.GetOrAdd(circuitName, static _ => ProviderFailureState.Initial);

            if (state.HalfOpenProbeId is not null)
            {
                lease = default;
                error = $"{circuitName} provider circuit is half-open and its probe is already in progress.";
                return false;
            }

            var now = _timeProvider.GetUtcNow();
            if (state.OpenUntilUtc is not null && state.OpenUntilUtc > now)
            {
                lease = default;
                error = $"{circuitName} provider circuit is open after repeated transport, body, or protocol failures.";
                return false;
            }

            if (state.OpenUntilUtc is null)
            {
                lease = new CircuitRequestLease(state.Generation, null, IsTracked: true);
                error = null;
                return true;
            }

            var probeId = Guid.NewGuid();
            var halfOpen = state with
            {
                Generation = state.Generation + 1,
                OpenUntilUtc = null,
                HalfOpenProbeId = probeId,
                PublicSnapshot = CreateSnapshot(circuitName, CircuitState.HalfOpen)
            };
            if (_providerFailures.TryUpdate(circuitName, halfOpen, state))
            {
                lease = new CircuitRequestLease(halfOpen.Generation, probeId, IsTracked: true);
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
        if (!lease.IsTracked)
            return;

        while (_providerFailures.TryGetValue(circuitName, out var existing))
        {
            if (!LeaseMatches(existing, lease))
                return;

            var now = _timeProvider.GetUtcNow();
            ProviderFailureState next;
            if (lease.IsHalfOpenProbe)
            {
                next = new ProviderFailureState(
                    existing.Generation + 1,
                    settings.FailureThreshold,
                    now.AddSeconds(settings.BreakDurationSeconds),
                    null,
                    CreateSnapshot(circuitName, CircuitState.Open, reason));
            }
            else
            {
                if (existing.OpenUntilUtc is not null || existing.HalfOpenProbeId is not null)
                    return;

                var failureCount = existing.ConsecutiveFailures + 1;
                next = failureCount >= settings.FailureThreshold
                    ? new ProviderFailureState(
                        existing.Generation + 1,
                        failureCount,
                        now.AddSeconds(settings.BreakDurationSeconds),
                        null,
                        CreateSnapshot(circuitName, CircuitState.Open, reason))
                    : existing with { ConsecutiveFailures = failureCount };
            }

            if (_providerFailures.TryUpdate(circuitName, next, existing))
                return;
        }
    }

    internal void RecordProviderSuccess(string circuitName, CircuitRequestLease lease)
    {
        if (!lease.IsTracked)
            return;

        while (_providerFailures.TryGetValue(circuitName, out var existing))
        {
            if (!LeaseMatches(existing, lease))
                return;

            ProviderFailureState next;
            if (lease.IsHalfOpenProbe)
            {
                next = new ProviderFailureState(
                    existing.Generation + 1,
                    0,
                    null,
                    null,
                    CreateSnapshot(circuitName, CircuitState.Closed));
            }
            else
            {
                if (existing.OpenUntilUtc is not null || existing.HalfOpenProbeId is not null)
                    return;

                next = existing with
                {
                    ConsecutiveFailures = 0,
                    PublicSnapshot = existing.ConsecutiveFailures > 0
                        ? CreateSnapshot(circuitName, CircuitState.Closed)
                        : existing.PublicSnapshot
                };
            }

            if (_providerFailures.TryUpdate(circuitName, next, existing))
                return;
        }
    }

    internal void AbandonProviderRequest(
        string circuitName,
        CircuitBreakerSettings settings,
        CircuitRequestLease lease)
    {
        if (!lease.IsTracked || !lease.IsHalfOpenProbe)
            return;

        while (_providerFailures.TryGetValue(circuitName, out var existing))
        {
            if (!LeaseMatches(existing, lease))
                return;

            var now = _timeProvider.GetUtcNow();
            var reopened = new ProviderFailureState(
                existing.Generation + 1,
                Math.Max(existing.ConsecutiveFailures, settings.FailureThreshold),
                now.AddSeconds(settings.BreakDurationSeconds),
                null,
                CreateSnapshot(
                    circuitName,
                    CircuitState.Open,
                    "Half-open provider probe was abandoned."));
            if (_providerFailures.TryUpdate(circuitName, reopened, existing))
                return;
        }
    }

    private CircuitBreakerSnapshot CreateSnapshot(
        string circuitName,
        CircuitState state,
        string? lastFailureReason = null) =>
        new(circuitName, state, _timeProvider.GetUtcNow(), lastFailureReason);

    private static bool LeaseMatches(ProviderFailureState state, CircuitRequestLease lease) =>
        state.Generation == lease.Generation &&
        state.HalfOpenProbeId == lease.HalfOpenProbeId;

    private static CircuitBreakerSnapshot? SelectMostRestrictive(
        CircuitBreakerSnapshot? first,
        CircuitBreakerSnapshot? second)
    {
        if (first is null)
            return second;
        if (second is null)
            return first;

        var firstRank = GetRestrictiveness(first.State);
        var secondRank = GetRestrictiveness(second.State);
        if (firstRank != secondRank)
            return firstRank > secondRank ? first : second;

        return first.LastTransitionUtc >= second.LastTransitionUtc ? first : second;
    }

    private static int GetRestrictiveness(CircuitState state) => state switch
    {
        CircuitState.Open => 2,
        CircuitState.HalfOpen => 1,
        _ => 0
    };

    private sealed record ProviderFailureState(
        long Generation,
        int ConsecutiveFailures,
        DateTimeOffset? OpenUntilUtc,
        Guid? HalfOpenProbeId,
        CircuitBreakerSnapshot? PublicSnapshot)
    {
        public static ProviderFailureState Initial { get; } = new(0, 0, null, null, null);
    }
}

internal readonly record struct CircuitRequestLease(
    long Generation,
    Guid? HalfOpenProbeId,
    bool IsTracked)
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
