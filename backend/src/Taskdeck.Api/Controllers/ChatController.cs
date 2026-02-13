using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/llm/chat")]
public class ChatController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly IUserContext _userContext;

    public ChatController(IChatService chatService, IUserContext userContext)
    {
        _chatService = chatService;
        _userContext = userContext;
    }

    [HttpPost("sessions")]
    public async Task<IActionResult> CreateSession([FromBody] CreateChatSessionDto dto, CancellationToken ct = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _chatService.CreateSessionAsync(userId, dto, ct);

        if (!result.IsSuccess)
        {
            return result.ErrorCode switch
            {
                "ValidationError" => BadRequest(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "NotFound" => NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                _ => Problem(result.ErrorMessage, statusCode: 500)
            };
        }

        return CreatedAtAction(nameof(GetSession), new { id = result.Value.Id }, result.Value);
    }

    [HttpGet("sessions/{id}")]
    public async Task<IActionResult> GetSession(Guid id, CancellationToken ct = default)
    {
        var result = await _chatService.GetSessionAsync(id, ct);

        if (!result.IsSuccess)
        {
            return result.ErrorCode == "NotFound"
                ? NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage })
                : Problem(result.ErrorMessage, statusCode: 500);
        }

        return Ok(result.Value);
    }

    [HttpPost("sessions/{id}/messages")]
    public async Task<IActionResult> SendMessage(Guid id, [FromBody] SendChatMessageDto dto, CancellationToken ct = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _chatService.SendMessageAsync(id, userId, dto, ct);

        if (!result.IsSuccess)
        {
            return result.ErrorCode switch
            {
                "ValidationError" => BadRequest(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                "NotFound" => NotFound(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                ErrorCodes.InvalidOperation => Conflict(new { errorCode = result.ErrorCode, message = result.ErrorMessage }),
                _ => Problem(result.ErrorMessage, statusCode: 500)
            };
        }

        return Ok(result.Value);
    }

    [HttpGet("sessions/{id}/stream")]
    public async Task GetStream(Guid id, CancellationToken ct = default)
    {
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        await foreach (var tokenEvent in _chatService.StreamResponseAsync(id, ct))
        {
            var eventType = tokenEvent.IsComplete ? "message.complete" : "message.delta";
            await Response.WriteAsync($"event: {eventType}\ndata: {System.Text.Json.JsonSerializer.Serialize(tokenEvent)}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
    }

    private bool TryGetCurrentUserId(out Guid userId, out IActionResult? errorResult)
    {
        userId = Guid.Empty;
        errorResult = null;

        if (!_userContext.IsAuthenticated || string.IsNullOrWhiteSpace(_userContext.UserId))
        {
            errorResult = Unauthorized(new
            {
                errorCode = ErrorCodes.AuthenticationFailed,
                message = "Authenticated user context is required"
            });
            return false;
        }

        if (!Guid.TryParse(_userContext.UserId, out userId))
        {
            errorResult = Unauthorized(new
            {
                errorCode = ErrorCodes.AuthenticationFailed,
                message = "Authenticated user id claim is invalid"
            });
            return false;
        }

        return true;
    }
}
