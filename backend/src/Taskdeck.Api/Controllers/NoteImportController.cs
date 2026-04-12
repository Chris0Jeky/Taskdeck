using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Extensions;
using Taskdeck.Api.RateLimiting;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

/// <summary>
/// Note-style import endpoints for markdown files and web clips.
/// All imported content routes through the standard capture pipeline —
/// no direct board mutations occur here.
/// </summary>
[ApiController]
[Authorize]
[Route("api/import/notes")]
[Produces("application/json")]
public class NoteImportController : AuthenticatedControllerBase
{
    private readonly INoteImportService _noteImportService;

    public NoteImportController(
        INoteImportService noteImportService,
        IUserContext userContext)
        : base(userContext)
    {
        _noteImportService = noteImportService;
    }

    /// <summary>
    /// Import a markdown file. The content is parsed into sections and
    /// each section becomes a capture item in the standard pipeline.
    /// </summary>
    /// <param name="dto">Markdown import request with filename and content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Import result with created capture item IDs.</returns>
    /// <response code="200">Markdown imported successfully — capture items created.</response>
    /// <response code="400">Validation error (empty content, oversized file, etc.).</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="429">Rate limit exceeded.</response>
    [HttpPost("markdown")]
    [EnableRateLimiting(RateLimitingPolicyNames.NoteImportPerUser)]
    [ProducesResponseType(typeof(NoteImportResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ImportMarkdown(
        [FromBody] MarkdownImportRequestDto? dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        if (dto == null)
        {
            return BadRequest(new ApiErrorResponse(
                "VALIDATION_ERROR",
                "Request body is required"));
        }

        var result = await _noteImportService.ImportMarkdownAsync(userId, dto, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Import a web clip (URL + content snippet). Creates a single capture
    /// item with the URL preserved as source provenance.
    /// </summary>
    /// <param name="dto">Web clip import request with URL, content, and optional title.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Import result with created capture item ID.</returns>
    /// <response code="200">Web clip imported successfully — capture item created.</response>
    /// <response code="400">Validation error (invalid URL, empty content, etc.).</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="429">Rate limit exceeded.</response>
    [HttpPost("webclip")]
    [EnableRateLimiting(RateLimitingPolicyNames.NoteImportPerUser)]
    [ProducesResponseType(typeof(NoteImportResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ImportWebClip(
        [FromBody] WebClipImportRequestDto? dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        if (dto == null)
        {
            return BadRequest(new ApiErrorResponse(
                "VALIDATION_ERROR",
                "Request body is required"));
        }

        var result = await _noteImportService.ImportWebClipAsync(userId, dto, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
}
