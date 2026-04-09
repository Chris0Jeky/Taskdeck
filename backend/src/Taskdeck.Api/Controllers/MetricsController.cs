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
    private readonly IMetricsExportService _exportService;

    public MetricsController(
        IBoardMetricsService metricsService,
        IMetricsExportService exportService,
        IUserContext userContext)
        : base(userContext)
    {
        _metricsService = metricsService;
        _exportService = exportService;
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

    /// <summary>
    /// Export board metrics as a downloadable CSV file.
    /// </summary>
    /// <param name="boardId">The board to export metrics for.</param>
    /// <param name="from">Start of date range (ISO 8601).</param>
    /// <param name="to">End of date range (ISO 8601).</param>
    /// <param name="labelId">Optional label filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>CSV file download.</returns>
    /// <response code="200">CSV file returned.</response>
    /// <response code="400">Invalid query parameters.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="403">No read access to the board.</response>
    /// <response code="404">Board not found.</response>
    [HttpGet("boards/{boardId}/export")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExportBoardMetrics(
        Guid boardId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] Guid? labelId,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        // Default to last 30 days if not specified
        var toDate = to ?? DateTimeOffset.UtcNow;
        var fromDate = from ?? toDate.AddDays(-30);

        var query = new BoardMetricsQuery(boardId, fromDate, toDate, labelId);
        var result = await _exportService.ExportCsvAsync(query, userId, cancellationToken);

        if (!result.IsSuccess)
            return result.ToErrorActionResult();

        var export = result.Value;
        return File(export.Content, export.ContentType, export.FileName);
    }
}
