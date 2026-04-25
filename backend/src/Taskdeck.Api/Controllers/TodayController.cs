using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

/// <summary>
/// Today view endpoints: streak data, dossier, and daily summaries.
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
[Produces("application/json")]
public class TodayController : AuthenticatedControllerBase
{
    private readonly IStreakService _streakService;

    public TodayController(
        IStreakService streakService,
        IUserContext userContext)
        : base(userContext)
    {
        _streakService = streakService;
    }

    /// <summary>
    /// Get streak data (daily activity intensity and sealed status) for the authenticated user.
    /// </summary>
    /// <param name="days">Number of days to include (1-365, default 90).</param>
    /// <returns>Streak data with daily intensity buckets and streak lengths.</returns>
    /// <response code="200">Streak data returned successfully.</response>
    /// <response code="400">Invalid days parameter.</response>
    /// <response code="401">Authentication required.</response>
    [HttpGet("streak")]
    [ProducesResponseType(typeof(StreakResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetStreak([FromQuery] int days = 90)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _streakService.GetStreakAsync(userId, days);

        if (!result.IsSuccess)
            return result.ToErrorActionResult();

        var streakResult = result.Value;
        var response = new StreakResponse(
            streakResult.Days.Select(d => new StreakDayResponse(d.Date, d.IsSealed, d.IntensityBucket)).ToList(),
            streakResult.CurrentStreakLength,
            streakResult.LongestStreakLength,
            streakResult.Days.Count);

        return Ok(response);
    }
}
