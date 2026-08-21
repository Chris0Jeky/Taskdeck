using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Interfaces;

public interface ILlmQueueRepository : IRepository<LlmRequest>
{
    /// <summary>
    /// Returns historical total capture progress plus active Inbox workload counts. Active counts
    /// exclude only captures whose effective extant board is archived; boardless/dangling records
    /// remain visible. <c>TotalCaptures</c> deliberately retains archived history for onboarding.
    /// </summary>
    Task<(int TotalCaptures, int NewCount, int FailedCount, int TriagingCount, int TriagedCount)> GetCaptureSummaryByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
    Task<IEnumerable<LlmRequest>> GetPendingAsync(int limit = 100, CancellationToken cancellationToken = default);
    Task<IEnumerable<LlmRequest>> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a single newest-first page of the user's capture requests (request type
    /// <c>inbox.capture.*</c>), bounded at the database with <c>LIMIT</c>/<c>OFFSET</c>. The capture
    /// predicate is applied in the query so the page is not diluted by non-capture rows, and a stable
    /// total order (CreatedAt desc, then Id) guarantees no row is skipped or duplicated across pages.
    /// Callers that need a user's full request history (GDPR export/delete) must keep using the
    /// unbounded <see cref="GetByUserAsync"/>; this method is for paged capture listings only.
    /// </summary>
    /// <param name="limit">Page size; must be at least 1.</param>
    /// <param name="offset">Rows to skip; must be non-negative.</param>
    /// <param name="boardId">
    /// Optional raw-board pre-filter (#1239). When supplied, the scan keeps only captures whose raw
    /// <c>BoardId</c> equals it OR is NULL — null-board captures are retained because they may still
    /// resolve to the target board via applied-conversion provenance, which is computed in the
    /// service layer. This sharply narrows board-filtered scans by excluding other boards' captures
    /// at the database. The effective-board (provenance) and status filters remain in the service.
    /// </param>
    Task<IEnumerable<LlmRequest>> GetCapturesByUserAsync(Guid userId, int limit, int offset, Guid? boardId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all requests with the given status, newest-first, unbounded. The health backlog gauge
    /// currently inspects every row through this (it should move to count primitives — tracked in #1251),
    /// so do NOT add a default cap here. Bounded background work-drains must use the type-aware
    /// <c>GetOldest*</c> methods below; a bounded DISPLAY listing must use
    /// <see cref="GetByStatusForDisplayAsync"/>.
    /// </summary>
    Task<IEnumerable<LlmRequest>> GetByStatusAsync(RequestStatus status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns at most <paramref name="limit"/> requests in a status, newest-first, bounded at the
    /// database — for DISPLAY / diagnostics (the CLI ops queue listing). Claim/processing paths must NOT
    /// use this: they require the type-aware <c>GetOldest*</c> primitives so bounding never starves
    /// non-capture work (#1195).
    /// </summary>
    Task<IEnumerable<LlmRequest>> GetByStatusForDisplayAsync(RequestStatus status, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns at most <paramref name="limit"/> Pending non-capture (automation) requests, oldest-first,
    /// with the non-capture predicate applied IN the query and bounded at the database. The predicate must
    /// live in the query, not in a post-fetch filter: untriaged capture requests also sit in the Pending
    /// queue and, being older, would otherwise fill an oldest-first window and starve automation work.
    /// </summary>
    /// <param name="limit">Maximum rows to return; must be at least 1.</param>
    Task<IEnumerable<LlmRequest>> GetOldestPendingNonCaptureAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns at most <paramref name="limit"/> Processing capture-triage requests, oldest-first, with the
    /// capture predicate applied in the query and bounded at the database (same anti-starvation reasoning as
    /// <see cref="GetOldestPendingNonCaptureAsync"/>: non-capture requests also transit Processing).
    /// Transcript-capture requests are EXCLUDED: they belong to the transcript worker lane
    /// (<see cref="GetOldestProcessingTranscriptAsync"/>) because their LLM-backed triage runs
    /// seconds-to-minutes and must never block the millisecond-latency capture lane (REVIVAL-08).
    /// </summary>
    /// <param name="limit">Maximum rows to return; must be at least 1.</param>
    Task<IEnumerable<LlmRequest>> GetOldestProcessingCaptureAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns at most <paramref name="limit"/> Processing transcript-capture requests
    /// (request type <c>inbox.capture.transcript.*</c>), oldest-first, bounded at the database.
    /// The transcript worker lane's fetch primitive (REVIVAL-08): transcript captures share the
    /// capture lifecycle (enqueued Pending, user-triggered triage marks Processing, worker re-claims
    /// from Processing) but are drained by their own worker so slow LLM triage cannot starve either
    /// the capture lane or the non-capture automation lane.
    /// </summary>
    /// <param name="limit">Maximum rows to return; must be at least 1.</param>
    Task<IEnumerable<LlmRequest>> GetOldestProcessingTranscriptAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>Counts Pending non-capture (automation) requests for backlog telemetry, without materializing rows.</summary>
    Task<int> CountPendingNonCaptureAsync(CancellationToken cancellationToken = default);

    /// <summary>Counts Processing capture-triage requests (excluding transcripts) for backlog telemetry, without materializing rows.</summary>
    Task<int> CountProcessingCaptureAsync(CancellationToken cancellationToken = default);

    /// <summary>Counts Processing transcript-capture requests for backlog telemetry, without materializing rows.</summary>
    Task<int> CountProcessingTranscriptAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts Pending capture-triage requests for the readiness gauge, without materializing rows.
    /// INCLUDES transcript captures: Pending means "in the inbox, triage not yet requested", which
    /// is a per-user inbox state, not a worker-lane state — the capture/transcript lane split only
    /// applies to Processing rows, where it decides which worker owns the row.
    /// </summary>
    Task<int> CountPendingCaptureAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns at most <paramref name="limit"/> NON-capture (automation) requests stuck in
    /// <see cref="RequestStatus.Processing"/> whose <c>UpdatedAt</c> is at or before
    /// <paramref name="staleBefore"/>, oldest-first, bounded at the database. These are requests a worker
    /// claimed (flipping them to Processing) but never completed or failed — typically because the worker
    /// crashed mid-flight — so nothing re-enqueues them and they are otherwise abandoned forever (#1209).
    /// Capture-triage requests are excluded by the in-query predicate: they are read from Processing every
    /// poll and re-claimed, so they self-heal; only non-capture work (read solely from Pending) needs a
    /// recovery sweep. The predicate must live in the query, not a post-fetch filter, so the bound never
    /// fills with rows the sweeper would discard.
    /// </summary>
    /// <param name="staleBefore">Only rows with <c>UpdatedAt &lt;= staleBefore</c> are returned.</param>
    /// <param name="limit">Maximum rows to return; must be at least 1.</param>
    Task<IReadOnlyList<LlmRequest>> GetStuckProcessingNonCaptureAsync(DateTimeOffset staleBefore, int limit, CancellationToken cancellationToken = default);
    Task<IEnumerable<LlmRequest>> GetByUserAndStatusAsync(Guid userId, RequestStatus status, CancellationToken cancellationToken = default);
    Task<Dictionary<RequestStatus, int>> GetStatusCountsByUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<LlmRequest?> GetNextPendingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically claims a Processing plain capture request (request type
    /// <c>inbox.capture.*</c>, excluding transcript captures) for the capture worker lane using
    /// optimistic concurrency: stamps UpdatedAt only if the row still has the expected UpdatedAt.
    /// Returns true if the claim succeeded. On success, implementations must refresh any in-memory
    /// instance of the request they hold so callers observe the persisted claim timestamp.
    /// </summary>
    Task<bool> TryClaimProcessingCaptureAsync(
        Guid requestId,
        DateTimeOffset expectedUpdatedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically claims a Processing transcript-capture request (request type
    /// <c>inbox.capture.transcript.*</c>) for the transcript worker lane using optimistic
    /// concurrency: stamps UpdatedAt only if the row still has the expected UpdatedAt. Mutually
    /// exclusive with <see cref="TryClaimProcessingCaptureAsync"/> via the in-query lane predicate,
    /// so the capture and transcript workers can never claim each other's rows (REVIVAL-08).
    /// </summary>
    Task<bool> TryClaimProcessingTranscriptAsync(
        Guid requestId,
        DateTimeOffset expectedUpdatedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically claims a pending non-capture request for processing using optimistic concurrency.
    /// Sets Status from Pending to Processing and updates UpdatedAt, only if the row still has the
    /// expected Status (Pending) and UpdatedAt values. Returns true if the claim succeeded.
    /// On success, implementations must refresh any in-memory instance of the request they hold
    /// (e.g. one materialized by an earlier query) so callers observe the claimed Processing state.
    /// </summary>
    Task<bool> TryClaimProcessingAsync(
        Guid requestId,
        DateTimeOffset expectedUpdatedAt,
        CancellationToken cancellationToken = default);
}
