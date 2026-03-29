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
    Task<LlmRequest?> GetNextPendingAsync(CancellationToken cancellationToken = default);
    Task<bool> TryClaimProcessingCaptureAsync(
        Guid requestId,
        DateTimeOffset expectedUpdatedAt,
        CancellationToken cancellationToken = default);
}
