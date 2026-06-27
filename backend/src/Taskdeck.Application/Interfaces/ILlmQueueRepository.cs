using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Interfaces;

public interface ILlmQueueRepository : IRepository<LlmRequest>
{
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
    Task<IEnumerable<LlmRequest>> GetCapturesByUserAsync(Guid userId, int limit, int offset, CancellationToken cancellationToken = default);

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
    /// </summary>
    /// <param name="limit">Maximum rows to return; must be at least 1.</param>
    Task<IEnumerable<LlmRequest>> GetOldestProcessingCaptureAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>Counts Pending non-capture (automation) requests for backlog telemetry, without materializing rows.</summary>
    Task<int> CountPendingNonCaptureAsync(CancellationToken cancellationToken = default);

    /// <summary>Counts Processing capture-triage requests for backlog telemetry, without materializing rows.</summary>
    Task<int> CountProcessingCaptureAsync(CancellationToken cancellationToken = default);

    /// <summary>Counts Pending capture-triage requests for the readiness gauge, without materializing rows.</summary>
    Task<int> CountPendingCaptureAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<LlmRequest>> GetByUserAndStatusAsync(Guid userId, RequestStatus status, CancellationToken cancellationToken = default);
    Task<Dictionary<RequestStatus, int>> GetStatusCountsByUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<LlmRequest?> GetNextPendingAsync(CancellationToken cancellationToken = default);
    Task<bool> TryClaimProcessingCaptureAsync(
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
