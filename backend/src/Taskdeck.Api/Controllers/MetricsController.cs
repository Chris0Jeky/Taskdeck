using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

/// <summary>
/// Board metrics endpoints: throughput, cycle time, WIP, blocked trends.
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
[Produces("application/json")]
public class MetricsController : AuthenticatedControllerBase
{
    private readonly IBoardMetricsService _metricsService;

    public MetricsController(IBoardMetricsService metricsService, IUserContext userContext)
        : base(userContext)
    {
        _metricsService = metricsService;
    }

    /// <summary>
    /// Get board metrics (throughput, cycle time, WIP, blocked) for a date range.
    /// </summary>
    /// <param name="boardId">The board to compute metrics for.</param>
    /// <param name="from">Start of date range (ISO 8601).</param>
    /// <param name="to">End of date range (ISO 8601).</param>
    /// <param name="labelId">Optional label filter.</param>
    /// <returns>Aggregated board metrics.</returns>
    /// <response code="200">Metrics computed successfully.</response>
    /// <response code="400">Invalid query parameters.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="403">No read access to the board.</response>
    /// <response code="404">Board not found.</response>
    [HttpGet("boards/{boardId}")]
    [ProducesResponseType(typeof(BoardMetricsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBoardMetrics(
        Guid boardId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] Guid? labelId)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        // Default to last 30 days if not specified
        var toDate = to ?? DateTimeOffset.UtcNow;
        var fromDate = from ?? toDate.AddDays(-30);

        var query = new BoardMetricsQuery(boardId, fromDate, toDate, labelId);
        var result = await _metricsService.GetBoardMetricsAsync(query, userId);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
}
