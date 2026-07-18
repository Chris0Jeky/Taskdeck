namespace Taskdeck.Application.Services;

/// <summary>
/// Bounds the number of artefact-extraction parse workers that may run
/// concurrently. Permits track parse-THREAD occupancy, not request lifetime: an
/// abandoned parse-bomb thread (one that never observes cancellation and keeps
/// spinning after its request has timed out and returned) continues to hold its
/// permit until it actually finishes. That is the point — it caps how many
/// concurrently-abandoned parses can accumulate. When every permit is held, a new
/// extraction is rejected pre-parse (<see cref="TryAcquire"/> returns
/// <c>false</c>) rather than queued, because a queue in front of permits held by
/// spinning bombs is just a second unbounded backlog.
/// </summary>
public sealed class ArtefactExtractionGate : IDisposable
{
    private readonly SemaphoreSlim _semaphore;

    public ArtefactExtractionGate(ArtefactStorageSettings? settings = null)
    {
        MaxConcurrency = (settings ?? new ArtefactStorageSettings()).ExtractionMaxConcurrency;
        // maxCount == MaxConcurrency turns an over-release (a double-free bug) into an
        // immediate SemaphoreFullException instead of silently inflating capacity.
        _semaphore = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);
    }

    /// <summary>Maximum number of parse workers permitted to run at once.</summary>
    public int MaxConcurrency { get; }

    /// <summary>Permits currently available (diagnostic / test observation only).</summary>
    public int AvailablePermits => _semaphore.CurrentCount;

    /// <summary>
    /// Try to take a permit without blocking. Returns <c>true</c> when a permit was
    /// acquired (the caller must later call <see cref="Release"/> exactly once);
    /// <c>false</c> when the gate is saturated.
    /// </summary>
    public bool TryAcquire() => _semaphore.Wait(0);

    /// <summary>Return a permit previously taken by <see cref="TryAcquire"/>.</summary>
    public void Release()
    {
        _semaphore.Release();
        PermitReleased?.Invoke();
    }

    /// <summary>
    /// Test-only observation seam: raised after each permit is released. Lets a test
    /// await the release that happens inside the service's abandoned-worker
    /// completion continuation without polling. Never wired in production.
    /// </summary>
    internal event Action? PermitReleased;

    public void Dispose() => _semaphore.Dispose();
}
