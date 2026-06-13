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
    Task<IEnumerable<LlmRequest>> GetByStatusAsync(RequestStatus status, CancellationToken cancellationToken = default);
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
