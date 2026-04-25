namespace Taskdeck.Application.Services;

/// <summary>
/// Drives embedding backfill for entities that have not yet been indexed.
/// Implementations must be resumable (track progress) and failure-safe
/// (individual item failures do not block the batch).
/// </summary>
public interface IEmbeddingBackfillService
{
    /// <summary>
    /// Processes a single batch of un-embedded items. Returns the number
    /// of items successfully embedded in this batch (0 when caught up).
    /// </summary>
    Task<BackfillBatchResult> ProcessBatchAsync(
        int batchSize,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a single backfill batch execution.
/// </summary>
public sealed record BackfillBatchResult(
    int Processed,
    int Failed,
    int Remaining);
