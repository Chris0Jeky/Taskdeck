using Microsoft.AspNetCore.Mvc;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Route("api/llm-queue")]
public class LlmQueueController : ControllerBase
{
    private readonly LlmQueueService _llmQueueService;

    public LlmQueueController(LlmQueueService llmQueueService)
    {
        _llmQueueService = llmQueueService;
    }

    [HttpPost]
    public async Task<IActionResult> AddToQueue([FromBody] CreateLlmRequestDto dto)
    {
        var result = await _llmQueueService.AddToQueueAsync(dto);

        if (!result.IsSuccess)
        {
            return result.ErrorCode switch
            {
                "NotFound" => NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "ValidationError" => BadRequest(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "AuthenticationFailed" => Unauthorized(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "Forbidden" => StatusCode(403, new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "Conflict" => Conflict(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                _ => Problem(result.ErrorMessage, statusCode: 500)
            };
        }

        return Ok(result.Value);
    }

    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetUserQueue(Guid userId)
    {
        var result = await _llmQueueService.GetUserQueueAsync(userId);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.ErrorMessage, statusCode: 500);
    }

    [HttpGet("status/{status}")]
    public async Task<IActionResult> GetByStatus(string status)
    {
        if (!Enum.TryParse<RequestStatus>(status, true, out var parsedStatus) || !Enum.IsDefined(parsedStatus))
            return BadRequest(new { errorCode = "ValidationError", message = $"Invalid status value: {status}" });

        var result = await _llmQueueService.GetQueueByStatusAsync(parsedStatus);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.ErrorMessage, statusCode: 500);
    }

    [HttpPost("{requestId}/cancel")]
    public async Task<IActionResult> CancelRequest(Guid requestId, [FromQuery] Guid userId)
    {
        var result = await _llmQueueService.CancelRequestAsync(requestId, userId);

        if (!result.IsSuccess)
        {
            return result.ErrorCode switch
            {
                "NotFound" => NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "ValidationError" => BadRequest(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "AuthenticationFailed" => Unauthorized(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "Forbidden" => StatusCode(403, new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "Conflict" => Conflict(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                _ => Problem(result.ErrorMessage, statusCode: 500)
            };
        }

        return NoContent();
    }

    [HttpPost("process-next")]
    public async Task<IActionResult> ProcessNext()
    {
        var result = await _llmQueueService.ProcessNextRequestAsync();

        if (!result.IsSuccess)
        {
            return result.ErrorCode switch
            {
                "NotFound" => NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "ValidationError" => BadRequest(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "AuthenticationFailed" => Unauthorized(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "Forbidden" => StatusCode(403, new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "Conflict" => Conflict(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                _ => Problem(result.ErrorMessage, statusCode: 500)
            };
        }

        return Ok(result.Value);
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetQueueStats()
    {
        var result = await _llmQueueService.GetQueueStatsAsync();
        return result.IsSuccess ? Ok(result.Value) : Problem(result.ErrorMessage, statusCode: 500);
    }
}
