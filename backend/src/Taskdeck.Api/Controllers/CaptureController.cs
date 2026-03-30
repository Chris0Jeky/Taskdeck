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

[ApiController]
[Authorize]
[Route("api/capture/items")]
public class CaptureController : AuthenticatedControllerBase
{
    private readonly ICaptureService _captureService;

    public CaptureController(ICaptureService captureService, IUserContext userContext)
        : base(userContext)
    {
        _captureService = captureService;
    }

    [HttpPost]
    [EnableRateLimiting(RateLimitingPolicyNames.CaptureWritePerUser)]
    public async Task<IActionResult> Create([FromBody] CreateCaptureItemDto dto, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _captureService.CreateAsync(userId, dto, cancellationToken);
        if (!result.IsSuccess)
            return result.ToErrorActionResult();

        return CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value);
    }

    [HttpGet]
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

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _captureService.GetByIdAsync(userId, id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpPost("{id:guid}/ignore")]
    public async Task<IActionResult> Ignore(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _captureService.IgnoreAsync(userId, id, cancellationToken);
        return result.IsSuccess ? NoContent() : result.ToErrorActionResult();
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _captureService.CancelAsync(userId, id, cancellationToken);
        return result.IsSuccess ? NoContent() : result.ToErrorActionResult();
    }

    [HttpPost("{id:guid}/triage")]
    [EnableRateLimiting(RateLimitingPolicyNames.CaptureWritePerUser)]
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
