using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

/// <summary>
/// The persistence seam the ID-preserving capture backfill needs on top of
/// <see cref="ICaptureStore"/> (CF-01 <c>#2255</c>): a batch of legacy capture queue rows that have
/// no durable <see cref="Capture"/> yet, and the marker that records whether the backfill has
/// finished on this database.
/// <para>
/// The backlog query is an anti-join, not a cursor. That is what makes the backfill both idempotent
/// and resumable with no extra state: a row leaves the backlog the moment its capture is committed,
/// so re-running creates nothing twice and a crash mid-way resumes from wherever the last committed
/// batch left off.
/// </para>
/// </summary>
public interface ICaptureBackfillStore
{
    /// <summary>
    /// Returns at most <paramref name="batchSize"/> capture-shaped <see cref="LlmRequest"/> rows
    /// (request type <c>inbox.capture.%</c>) that have no <see cref="Capture"/> under the same id,
    /// oldest first. Detached: the backfill reads them, it never writes them.
    /// </summary>
    Task<IReadOnlyList<LlmRequest>> GetLegacyCaptureBacklogAsync(
        int batchSize,
        CancellationToken cancellationToken = default);

    /// <summary>Counts the rows still to migrate, without materialising them.</summary>
    Task<int> CountLegacyCaptureBacklogAsync(CancellationToken cancellationToken = default);

    /// <summary>The persisted marker for <paramref name="key"/>, or null when the backfill never ran here.</summary>
    Task<CaptureBackfillState?> GetStateAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages the marker (insert or update) into the ambient unit of work, so a batch's captures and
    /// the progress they represent commit together or not at all.
    /// </summary>
    Task SaveStateAsync(CaptureBackfillState state, CancellationToken cancellationToken = default);
}
