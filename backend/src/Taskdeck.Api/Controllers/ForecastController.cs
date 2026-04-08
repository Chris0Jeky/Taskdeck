using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

/// <summary>
/// Heuristic forecasting endpoints: estimated completion dates and capacity guidance.
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
[Produces("application/json")]
public class ForecastController : AuthenticatedControllerBase
{
    private readonly IForecastingService _forecastingService;

    public ForecastController(IForecastingService forecastingService, IUserContext userContext)
        : base(userContext)
    {
        _forecastingService = forecastingService;
    }

    /// <summary>
    /// Get a heuristic forecast for a board: estimated completion date,
    /// confidence bands, throughput statistics, and explainable assumptions.
    /// </summary>
    /// <param name="boardId">The board to forecast.</param>
    /// <param name="historyDays">
    /// Number of days of history to use for throughput calculation (1–365, default 30).
    /// </param>
    /// <returns>Board forecast with confidence bands and assumptions.</returns>
    /// <response code="200">Forecast computed successfully.</response>
    /// <response code="400">Invalid query parameters.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="403">No read access to the board.</response>
    /// <response code="404">Board not found.</response>
    [HttpGet("board/{boardId}")]
    [ProducesResponseType(typeof(BoardForecastResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBoardForecast(
        Guid boardId,
        [FromQuery] int? historyDays)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var query = new BoardForecastQuery(boardId, historyDays);
        var result = await _forecastingService.GetBoardForecastAsync(query, userId);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
}
