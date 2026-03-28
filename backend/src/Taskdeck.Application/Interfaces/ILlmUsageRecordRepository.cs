using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Interfaces;

public interface ILlmUsageRecordRepository : IRepository<LlmUsageRecord>
{
    Task<long> GetRequestCountAsync(
        Guid? userId,
        LlmSurface? surface,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    Task<long> GetTotalTokensAsync(
        Guid? userId,
        LlmSurface? surface,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    Task<(long TotalInputTokens, long TotalOutputTokens, long TotalRequests)> GetUsageSummaryAsync(
        Guid? userId,
        LlmSurface? surface,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
