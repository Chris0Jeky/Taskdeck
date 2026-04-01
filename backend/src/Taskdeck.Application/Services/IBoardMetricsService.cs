using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

public interface IBoardMetricsService
{
    /// <summary>
    /// Compute board metrics (throughput, cycle time, WIP, blocked) for the
    /// given board and date range.  The acting user must have read access.
    /// </summary>
    Task<Result<BoardMetricsResponse>> GetBoardMetricsAsync(
        BoardMetricsQuery query,
        Guid actingUserId,
        CancellationToken cancellationToken = default);
}
