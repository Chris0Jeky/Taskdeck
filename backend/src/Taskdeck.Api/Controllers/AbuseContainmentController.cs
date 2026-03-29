using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/abuse")]
public class AbuseContainmentController : AuthenticatedControllerBase
{
    private readonly IAbuseDetectionService _abuseDetectionService;

    public AbuseContainmentController(
        IAbuseDetectionService abuseDetectionService,
        IUserContext userContext) : base(userContext)
    {
        _abuseDetectionService = abuseDetectionService;
    }

    /// <summary>
    /// Get the current abuse status for a specific actor (user).
    /// </summary>
    [HttpGet("actors/{actorUserId:guid}/status")]
    public async Task<IActionResult> GetActorStatus(Guid actorUserId, CancellationToken ct = default)
    {
        if (!TryGetCurrentUserId(out _, out var errorResult))
            return errorResult!;

        var status = await _abuseDetectionService.GetActorStatusAsync(actorUserId, ct);
        return Ok(status);
    }

    /// <summary>
    /// Get the abuse event audit trail for a specific actor.
    /// </summary>
    [HttpGet("actors/{actorUserId:guid}/events")]
    public async Task<IActionResult> GetAuditTrail(
        Guid actorUserId,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        if (!TryGetCurrentUserId(out _, out var errorResult))
            return errorResult!;

        if (limit < 1 || limit > 200)
            return BadRequest(new ApiErrorResponse(ErrorCodes.ValidationError, "Limit must be between 1 and 200"));

        var events = await _abuseDetectionService.GetAuditTrailAsync(actorUserId, limit, ct);
        return Ok(events);
    }

    /// <summary>
    /// Operator override: set an actor to any abuse state.
    /// Used for both escalation and de-escalation (including clearing/unblocking).
    /// </summary>
    [HttpPost("actors/override")]
    public async Task<IActionResult> OverrideActorState(
        [FromBody] AbuseOverrideRequestDto dto,
        CancellationToken ct = default)
    {
        if (!TryGetCurrentUserId(out var operatorUserId, out var errorResult))
            return errorResult!;

        if (dto.ActorUserId == Guid.Empty)
            return BadRequest(new ApiErrorResponse(ErrorCodes.ValidationError, "Actor user ID is required"));

        if (string.IsNullOrWhiteSpace(dto.Reason))
            return BadRequest(new ApiErrorResponse(ErrorCodes.ValidationError, "Override reason is required"));

        var result = await _abuseDetectionService.OverrideActorStateAsync(
            dto.ActorUserId,
            dto.NewState,
            dto.Reason,
            operatorUserId,
            ct);

        if (!result.IsSuccess)
            return result.ToErrorActionResult();

        var status = await _abuseDetectionService.GetActorStatusAsync(dto.ActorUserId, ct);
        return Ok(status);
    }

    /// <summary>
    /// Trigger abuse evaluation for a specific actor.
    /// Evaluates recent LLM usage against abuse thresholds.
    /// </summary>
    [HttpPost("actors/{actorUserId:guid}/evaluate")]
    public async Task<IActionResult> EvaluateActor(Guid actorUserId, CancellationToken ct = default)
    {
        if (!TryGetCurrentUserId(out _, out var errorResult))
            return errorResult!;

        var signalsDetected = await _abuseDetectionService.EvaluateActorAsync(actorUserId, ct);
        var status = await _abuseDetectionService.GetActorStatusAsync(actorUserId, ct);

        return Ok(new { SignalsDetected = signalsDetected, Status = status });
    }
}
