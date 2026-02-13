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
[Route("api/llm/chat")]
public class ChatController : AuthenticatedControllerBase
{
    private readonly IChatService _chatService;

    public ChatController(IChatService chatService, IUserContext userContext) : base(userContext)
    {
        _chatService = chatService;
    }

    [HttpPost("sessions")]
    public async Task<IActionResult> CreateSession([FromBody] CreateChatSessionDto dto, CancellationToken ct = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _chatService.CreateSessionAsync(userId, dto, ct);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetSession), new { id = result.Value.Id }, result.Value)
            : result.ToErrorActionResult();
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> GetMySessions(CancellationToken ct = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _chatService.GetUserSessionsAsync(userId, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpGet("sessions/{id}")]
    public async Task<IActionResult> GetSession(Guid id, CancellationToken ct = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _chatService.GetSessionAsync(id, userId, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpPost("sessions/{id}/messages")]
    public async Task<IActionResult> SendMessage(Guid id, [FromBody] SendChatMessageDto dto, CancellationToken ct = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _chatService.SendMessageAsync(id, userId, dto, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpGet("sessions/{id}/stream")]
    public async Task GetStream(Guid id, CancellationToken ct = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            await Response.WriteAsJsonAsync(new ApiErrorResponse(
                ErrorCodes.AuthenticationFailed,
                "Authenticated user context is required"), ct);
            return;
        }

        var sessionResult = await _chatService.GetSessionAsync(id, userId, ct);
        if (!sessionResult.IsSuccess)
        {
            Response.StatusCode = sessionResult.ToHttpStatusCode();
            await Response.WriteAsJsonAsync(ApiErrorResponse.FromResult(sessionResult), ct);
            return;
        }

        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        await foreach (var tokenEvent in _chatService.StreamResponseAsync(id, userId, ct))
        {
            var eventType = tokenEvent.IsComplete ? "message.complete" : "message.delta";
            await Response.WriteAsync($"event: {eventType}\ndata: {System.Text.Json.JsonSerializer.Serialize(tokenEvent)}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
    }
}
