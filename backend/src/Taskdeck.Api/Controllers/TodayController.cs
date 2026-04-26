using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

/// <summary>
/// Today-view endpoints: cadence aggregation and daily dossier data.
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
[Produces("application/json")]
public class TodayController : AuthenticatedControllerBase
{
    private readonly ICadenceService _cadenceService;
    private readonly ITomorrowNoteService _tomorrowNoteService;

    public TodayController(
        ICadenceService cadenceService,
        ITomorrowNoteService tomorrowNoteService,
        IUserContext userContext)
        : base(userContext)
    {
        _cadenceService = cadenceService;
        _tomorrowNoteService = tomorrowNoteService;
    }

    /// <summary>
    /// Get the per-hour cadence snapshot for the authenticated user on the specified date.
    /// Returns 24 hourly buckets with event counts plus first/peak/last action timestamps.
    /// </summary>
    /// <param name="date">Date to aggregate (ISO 8601). Defaults to today (UTC).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Cadence snapshot for the day.</returns>
    /// <response code="200">Cadence snapshot returned successfully.</response>
    /// <response code="400">Invalid date parameter.</response>
    /// <response code="401">Authentication required.</response>
    [HttpGet("cadence")]
    [ProducesResponseType(typeof(CadenceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetCadence(
        [FromQuery] DateTimeOffset? date,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var targetDate = date ?? DateTimeOffset.UtcNow;

        var result = await _cadenceService.GetDailyCadenceAsync(userId, targetDate, cancellationToken);

        if (!result.IsSuccess)
            return result.ToErrorActionResult();

        var snapshot = result.Value;
        var response = new CadenceResponse(
            Buckets: snapshot.Buckets.Select(b => new CadenceBucketDto(b.Hour, b.EventCount)).ToList(),
            FirstActionAt: snapshot.FirstActionAt,
            PeakHour: snapshot.PeakHour,
            LastActionAt: snapshot.LastActionAt);

        return Ok(response);
    }

    /// <summary>
    /// Gets the tomorrow note for the given date.
    /// The note was written the previous day and is displayed on the specified date's morning open.
    /// </summary>
    [HttpGet("tomorrow-note")]
    [ProducesResponseType(typeof(TomorrowNoteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetTomorrowNote(
        [FromQuery] DateOnly date,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _tomorrowNoteService.GetNoteAsync(userId, date, cancellationToken);
        if (!result.IsSuccess)
            return result.ToErrorActionResult();

        if (result.Value is null)
            return NoContent();

        return Ok(result.Value);
    }

    /// <summary>
    /// Upsert the tomorrow note for the given date.
    /// Idempotent PUT suitable for debounced autosave from the frontend.
    /// </summary>
    [HttpPut("tomorrow-note")]
    [ProducesResponseType(typeof(TomorrowNoteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SaveTomorrowNote(
        [FromBody] SaveTomorrowNoteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _tomorrowNoteService.SaveNoteAsync(
            userId, request.Date, request.Text, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
}
