using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/today")]
public class TodayController : AuthenticatedControllerBase
{
    private readonly ITomorrowNoteService _tomorrowNoteService;

    public TodayController(
        ITomorrowNoteService tomorrowNoteService,
        IUserContext userContext) : base(userContext)
    {
        _tomorrowNoteService = tomorrowNoteService;
    }

    /// <summary>
    /// Gets the tomorrow note for the given date.
    /// The note was written the previous day and is displayed on the specified date's morning open.
    /// </summary>
    [HttpGet("tomorrow-note")]
    [ProducesResponseType(typeof(TomorrowNoteResponse), StatusCodes.Status200OK)]
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
            return Ok(new { });

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
