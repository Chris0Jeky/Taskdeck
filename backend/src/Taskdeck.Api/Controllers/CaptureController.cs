using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Extensions;
using Taskdeck.Api.RateLimiting;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Api.Controllers;

/// <summary>
/// Capture pipeline — the entry point for quick-capture of ideas, tasks, and notes.
/// Captured items flow through triage to generate automation proposals that appear
/// in the review queue for user approval before any board mutation occurs.
/// </summary>
[ApiController]
[Authorize]
[Route("api/capture/items")]
[Produces("application/json")]
public class CaptureController : AuthenticatedControllerBase
{
    private readonly ICaptureService _captureService;

    public CaptureController(ICaptureService captureService, IUserContext userContext)
        : base(userContext)
    {
        _captureService = captureService;
    }

    /// <summary>
    /// Create a new capture item. This is the primary quick-capture endpoint.
    /// </summary>
    /// <param name="dto">Capture item content including text and optional board target.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The newly created capture item.</returns>
    /// <response code="201">Capture item created successfully.</response>
    /// <response code="400">Validation error.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="429">Rate limit exceeded.</response>
    [HttpPost]
    [EnableRateLimiting(RateLimitingPolicyNames.CaptureWritePerUser)]
    [ProducesResponseType(typeof(CaptureItemDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Create([FromBody] CreateCaptureItemDto dto, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _captureService.CreateAsync(userId, dto, cancellationToken);
        if (!result.IsSuccess)
            return result.ToErrorActionResult();

        return CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value);
    }

    /// <summary>
    /// List capture items for the current user with optional filters.
    /// </summary>
    /// <param name="status">Filter by capture status (e.g., Pending, Triaging, Processed, Ignored, Cancelled).</param>
    /// <param name="boardId">Filter by target board.</param>
    /// <param name="limit">Maximum number of items to return (default 50).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of capture items.</returns>
    /// <response code="200">Returns the capture items.</response>
    /// <response code="400">Invalid status value.</response>
    /// <response code="401">Authentication required.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<CaptureItemSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> List(
        [FromQuery] string? status = null,
        [FromQuery] Guid? boardId = null,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        CaptureStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<CaptureStatus>(status, true, out var parsed) || !Enum.IsDefined(typeof(CaptureStatus), parsed))
            {
                return BadRequest(new ApiErrorResponse(
                    ErrorCodes.ValidationError,
                    $"Invalid capture status value: {status}"));
            }

            parsedStatus = parsed;
        }

        var filter = new CaptureListFilterDto(parsedStatus, boardId, limit);
        var result = await _captureService.ListAsync(userId, filter, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Get a single capture item by ID.
    /// </summary>
    /// <param name="id">The capture item identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The capture item.</returns>
    /// <response code="200">Returns the capture item.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="404">Capture item not found.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CaptureItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _captureService.GetByIdAsync(userId, id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Mark a capture item as ignored (dismissed without triage).
    /// </summary>
    /// <param name="id">The capture item identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">Item ignored successfully.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="404">Capture item not found.</response>
    [HttpPost("{id:guid}/ignore")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Ignore(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _captureService.IgnoreAsync(userId, id, cancellationToken);
        return result.IsSuccess ? NoContent() : result.ToErrorActionResult();
    }

    /// <summary>
    /// Cancel a capture item (e.g., if it was submitted in error).
    /// </summary>
    /// <param name="id">The capture item identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">Item cancelled successfully.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="404">Capture item not found.</response>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _captureService.CancelAsync(userId, id, cancellationToken);
        return result.IsSuccess ? NoContent() : result.ToErrorActionResult();
    }

    /// <summary>
    /// Enqueue a capture item for triage. The system will generate an automation
    /// proposal that the user must review before any board changes are applied.
    /// </summary>
    /// <param name="id">The capture item identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Triage enqueue result with status information.</returns>
    /// <response code="202">Triage enqueued — proposal will be generated asynchronously.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="404">Capture item not found.</response>
    /// <response code="429">Rate limit exceeded.</response>
    [HttpPost("{id:guid}/triage")]
    [EnableRateLimiting(RateLimitingPolicyNames.CaptureWritePerUser)]
    [ProducesResponseType(typeof(CaptureTriageEnqueueResultDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> EnqueueTriage(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _captureService.EnqueueTriageAsync(userId, id, cancellationToken);
        return result.IsSuccess ? Accepted(result.Value) : result.ToErrorActionResult();
    }

    [HttpPost("batch-triage")]
    [EnableRateLimiting(RateLimitingPolicyNames.CaptureWritePerUser)]
    public async Task<IActionResult> BatchTriage([FromBody] BatchTriageRequestDto dto, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _captureService.BatchTriageAsync(userId, dto, cancellationToken);
        if (!result.IsSuccess)
            return result.ToErrorActionResult();

        var batchResult = result.Value;
        if (batchResult.Failed > 0 && batchResult.Succeeded > 0)
            return StatusCode(207, batchResult);

        if (batchResult.Failed > 0 && batchResult.Succeeded == 0)
            return UnprocessableEntity(batchResult);

        return Ok(batchResult);
    }

    [HttpPut("{id:guid}/suggestion")]
    [EnableRateLimiting(RateLimitingPolicyNames.CaptureWritePerUser)]
    public async Task<IActionResult> UpdateSuggestion(Guid id, [FromBody] UpdateCaptureSuggestionDto dto, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _captureService.UpdateSuggestionAsync(userId, id, dto, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
}
