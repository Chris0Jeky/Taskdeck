using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

/// <summary>
/// The persistence seam the ID-preserving capture backfill needs on top of
/// <see cref="ICaptureStore"/> (CF-01 <c>#2255</c>): a batch of legacy capture queue rows that are
/// not yet faithfully represented by a durable <see cref="Capture"/>, and the marker that records
/// whether the backfill has finished on this database.
/// <para>
/// <b>The backlog is a divergence join, not an anti-join.</b> A row qualifies when it has no capture
/// under the same id <i>or</i> when the queue row has been written since its capture last was
/// (<c>LlmRequest.UpdatedAt &gt; Capture.UpdatedAt</c>). Missing rows alone are not enough: an
/// operator who turns <c>ContextFabric:DualWriteCaptures</c> off, lets a user edit a capture, and
/// turns it back on would otherwise leave the aggregate holding pre-edit text forever, and the read
/// switch would serve it. A divergent row is reconciled in place, and a changed source becomes a
/// superseding asset rather than a rewrite, so this is a reconcile pass and not only a first fill.
/// </para>
/// <para>
/// No cursor is needed, which is what makes the pass idempotent and resumable: a row leaves the
/// backlog the moment its capture agrees with it, so re-running creates nothing twice and a crash
/// mid-way resumes from wherever the last committed batch left off.
/// </para>
/// </summary>
public interface ICaptureBackfillStore
{
    /// <summary>
    /// Returns at most <paramref name="batchSize"/> capture-shaped <see cref="LlmRequest"/> rows
    /// (request type <c>inbox.capture.%</c>) that are missing a <see cref="Capture"/> or have
    /// diverged from one, oldest first. Detached: the backfill reads them, it never writes them.
    /// <para>
    /// <paramref name="excludedIds"/> holds the rows this run has already tried and failed to map.
    /// Without it, a poisoned row at the head of the oldest-first backlog would be re-read on every
    /// iteration and every healthy row behind it would never be reached.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<LlmRequest>> GetLegacyCaptureBacklogAsync(
        int batchSize,
        IReadOnlyCollection<Guid> excludedIds,
        CancellationToken cancellationToken = default);

    /// <summary>Counts the rows still outstanding, without materialising them.</summary>
    Task<int> CountLegacyCaptureBacklogAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases everything the unit of work is tracking once a batch has committed. A run walks the
    /// entire backlog through one scoped context, so without this the change tracker would hold
    /// every capture, asset and text payload it has saved - up to 200k characters each - until the
    /// run ended.
    /// </summary>
    Task ReleaseTrackedBatchAsync(CancellationToken cancellationToken = default);

    /// <summary>The persisted marker for <paramref name="key"/>, or null when the backfill never ran here.</summary>
    Task<CaptureBackfillState?> GetStateAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages the marker (insert or update) into the ambient unit of work, so a batch's captures and
    /// the progress they represent commit together or not at all.
    /// </summary>
    Task SaveStateAsync(CaptureBackfillState state, CancellationToken cancellationToken = default);
}
