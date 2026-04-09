using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Common;

namespace Taskdeck.Application.Services;

public interface IForecastingService
{
    /// <summary>
    /// Compute a heuristic forecast for the given board, estimating
    /// when remaining work will be completed based on historical throughput.
    /// </summary>
    Task<Result<BoardForecastResponse>> GetBoardForecastAsync(
        BoardForecastQuery query,
        Guid actingUserId,
        CancellationToken cancellationToken = default);
}
