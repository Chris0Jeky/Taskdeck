using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/workspace")]
public class WorkspaceController : AuthenticatedControllerBase
{
    private readonly IWorkspaceService _workspaceService;

    public WorkspaceController(
        IWorkspaceService workspaceService,
        IUserContext userContext) : base(userContext)
    {
        _workspaceService = workspaceService;
    }

    [HttpGet("home")]
    public async Task<IActionResult> GetHome(CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _workspaceService.GetHomeAsync(userId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpGet("today")]
    public async Task<IActionResult> GetToday(
        [FromQuery] string? localDate,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        if (!TryParseLocalDate(localDate, out var parsedLocalDate))
        {
            return BadRequest(new ApiErrorResponse(
                ErrorCodes.ValidationError,
                "The 'localDate' value must use YYYY-MM-DD format."));
        }

        var result = await _workspaceService.GetTodayAsync(userId, parsedLocalDate, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Get the authoritative collaboration-membership signal for the current user.
    /// Returns a count and a boolean only; no other user's identity is disclosed.
    /// </summary>
    /// <response code="200">Returns the caller's collaboration membership summary.</response>
    /// <response code="401">Authentication required.</response>
    [HttpGet("collaboration")]
    [ProducesResponseType(typeof(WorkspaceCollaborationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetCollaboration(CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _workspaceService.GetCollaborationAsync(userId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpGet("preferences")]
    public async Task<IActionResult> GetPreferences(CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _workspaceService.GetPreferencesAsync(userId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpPut("preferences")]
    public async Task<IActionResult> UpdatePreferences(
        [FromBody] UpdateWorkspacePreferenceDto dto,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _workspaceService.UpdatePreferencesAsync(userId, dto, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpPut("onboarding")]
    public async Task<IActionResult> UpdateOnboarding(
        [FromBody] UpdateWorkspaceOnboardingDto dto,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _workspaceService.UpdateOnboardingAsync(userId, dto, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Get cards with due dates within the specified date range across all accessible boards.
    /// Returns calendar items suitable for calendar/timeline visualization.
    /// </summary>
    /// <param name="from">Start of the date range (inclusive). Defaults to start of current month.</param>
    /// <param name="to">End of the date range (exclusive). Defaults to end of current month.</param>
    /// <param name="localDate">Caller's local calendar date in YYYY-MM-DD form. Defaults to the server's UTC date.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Returns calendar cards for the date range.</response>
    /// <response code="400">Invalid date range (from >= to or span > 90 days).</response>
    /// <response code="401">Authentication required.</response>
    [HttpGet("calendar")]
    [ProducesResponseType(typeof(WorkspaceCalendarDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetCalendar(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? localDate,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        if (!TryParseLocalDate(localDate, out var parsedLocalDate))
        {
            return BadRequest(new ApiErrorResponse(
                ErrorCodes.ValidationError,
                "The 'localDate' value must use YYYY-MM-DD format."));
        }

        var utcToday = DateOnly.FromDateTime(DateTime.UtcNow);
        var referenceDate = parsedLocalDate ?? utcToday;
        var effectiveFrom = from ?? new DateTimeOffset(
            referenceDate.Year,
            referenceDate.Month,
            1,
            0,
            0,
            0,
            TimeSpan.Zero);
        var effectiveTo = to ?? effectiveFrom.AddMonths(1);

        var result = await _workspaceService.GetCalendarAsync(
            userId,
            effectiveFrom,
            effectiveTo,
            parsedLocalDate,
            cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    private static bool TryParseLocalDate(string? value, out DateOnly? localDate)
    {
        if (value is null)
        {
            localDate = null;
            return true;
        }

        if (DateOnly.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            localDate = parsed;
            return true;
        }

        localDate = null;
        return false;
    }
}
