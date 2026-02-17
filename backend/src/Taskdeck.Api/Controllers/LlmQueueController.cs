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
[Route("api/llm-queue")]
public class LlmQueueController : AuthenticatedControllerBase
{
    private readonly LlmQueueService _llmQueueService;

    public LlmQueueController(LlmQueueService llmQueueService, IUserContext userContext)
        : base(userContext)
    {
        _llmQueueService = llmQueueService;
    }

    [HttpPost]
    public async Task<IActionResult> AddToQueue([FromBody] CreateLlmRequestDto dto)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _llmQueueService.AddToQueueAsync(userId, dto);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpGet("user")]
    public async Task<IActionResult> GetUserQueue()
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _llmQueueService.GetUserQueueAsync(userId);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpGet("status/{status}")]
    public async Task<IActionResult> GetByStatus(string status)
    {
        if (!Enum.TryParse<RequestStatus>(status, true, out var parsedStatus) || !Enum.IsDefined(parsedStatus))
            return BadRequest(new ApiErrorResponse(
                ErrorCodes.ValidationError,
                $"Invalid status value: {status}"));

        var result = await _llmQueueService.GetQueueByStatusAsync(parsedStatus);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpPost("{requestId}/cancel")]
    public async Task<IActionResult> CancelRequest(Guid requestId)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _llmQueueService.CancelRequestAsync(requestId, userId);
        return result.IsSuccess ? NoContent() : result.ToErrorActionResult();
    }

    [HttpPost("process-next")]
    public async Task<IActionResult> ProcessNext()
    {
        var result = await _llmQueueService.ProcessNextRequestAsync();
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetQueueStats()
    {
        var result = await _llmQueueService.GetQueueStatsAsync();
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
}
