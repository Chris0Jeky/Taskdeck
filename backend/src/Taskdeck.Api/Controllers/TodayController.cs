using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

/// <summary>
/// Today-view endpoints: cadence aggregation, daily dossier data, and day-seal operations.
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
[Produces("application/json")]
public class TodayController : AuthenticatedControllerBase
{
    private readonly ICadenceService _cadenceService;
    private readonly IDailySealService _dailySealService;

    public TodayController(
        ICadenceService cadenceService,
        IDailySealService dailySealService,
        IUserContext userContext)
        : base(userContext)
    {
        _cadenceService = cadenceService;
        _dailySealService = dailySealService;
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

    [HttpPost("seal")]
    public async Task<IActionResult> SealDay([FromBody] SealDayRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _dailySealService.SealDayAsync(userId, request.Date, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpGet("seal")]
    public async Task<IActionResult> GetSealStatus([FromQuery] DateOnly date, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _dailySealService.GetSealStatusAsync(userId, date, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
}

public sealed record SealDayRequest(DateOnly Date);
