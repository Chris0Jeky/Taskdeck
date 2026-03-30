using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Extensions;
using Taskdeck.Api.RateLimiting;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Api.Controllers;

/// <summary>
/// LLM-powered chat sessions. Messages can trigger automation proposals that
/// flow into the review queue. Supports real-time streaming via SSE.
/// </summary>
[ApiController]
[Authorize]
[Route("api/llm/chat")]
[Produces("application/json")]
public class ChatController : AuthenticatedControllerBase
{
    private readonly IChatService _chatService;

    public ChatController(IChatService chatService, IUserContext userContext) : base(userContext)
    {
        _chatService = chatService;
    }

    /// <summary>
    /// Create a new chat session, optionally scoped to a board.
    /// </summary>
    /// <param name="dto">Session parameters including title and optional board scope.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The newly created chat session.</returns>
    /// <response code="201">Chat session created successfully.</response>
    /// <response code="400">Validation error.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="429">Rate limit exceeded.</response>
    [HttpPost("sessions")]
    [EnableRateLimiting(RateLimitingPolicyNames.HotPathPerUser)]
    [ProducesResponseType(typeof(ChatSessionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> CreateSession([FromBody] CreateChatSessionDto dto, CancellationToken ct = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _chatService.CreateSessionAsync(userId, dto, ct);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetSession), new { id = result.Value.Id }, result.Value)
            : result.ToErrorActionResult();
    }

    /// <summary>
    /// List all chat sessions for the current user.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of chat sessions with recent messages.</returns>
    /// <response code="200">Returns the user's chat sessions.</response>
    /// <response code="401">Authentication required.</response>
    [HttpGet("sessions")]
    [ProducesResponseType(typeof(IEnumerable<ChatSessionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMySessions(CancellationToken ct = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _chatService.GetUserSessionsAsync(userId, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Check the health and availability of the configured LLM provider.
    /// </summary>
    /// <param name="probe">When true, sends a lightweight test request to the provider.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Provider health status including name, model, and availability.</returns>
    /// <response code="200">Returns provider health information.</response>
    /// <response code="401">Authentication required.</response>
    [HttpGet("health")]
    [EnableRateLimiting(RateLimitingPolicyNames.HotPathPerUser)]
    [ProducesResponseType(typeof(ChatProviderHealthDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetProviderHealth([FromQuery] bool probe = false, CancellationToken ct = default)
    {
        if (!TryGetCurrentUserId(out _, out var errorResult))
            return errorResult!;

        return Ok(await _chatService.GetProviderHealthAsync(probe, ct));
    }

    /// <summary>
    /// Get a chat session by ID, including recent messages.
    /// </summary>
    /// <param name="id">The chat session identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The chat session with recent messages.</returns>
    /// <response code="200">Returns the chat session.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="404">Chat session not found.</response>
    [HttpGet("sessions/{id}")]
    [ProducesResponseType(typeof(ChatSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSession(Guid id, CancellationToken ct = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _chatService.GetSessionAsync(id, userId, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Send a message in a chat session. The LLM responds and may generate
    /// automation proposals when RequestProposal is true.
    /// </summary>
    /// <param name="id">The chat session identifier.</param>
    /// <param name="dto">Message content and optional proposal request flag.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The assistant's response message.</returns>
    /// <response code="200">Message sent and response received.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="404">Chat session not found.</response>
    /// <response code="429">Rate limit exceeded.</response>
    [HttpPost("sessions/{id}/messages")]
    [EnableRateLimiting(RateLimitingPolicyNames.HotPathPerUser)]
    [ProducesResponseType(typeof(ChatMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SendMessage(Guid id, [FromBody] SendChatMessageDto dto, CancellationToken ct = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _chatService.SendMessageAsync(id, userId, dto, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    /// <summary>
    /// Stream the LLM response for a chat session via Server-Sent Events (SSE).
    /// Events are emitted as "message.delta" (partial tokens) and "message.complete" (final).
    /// </summary>
    /// <param name="id">The chat session identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">SSE stream of response tokens.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="404">Chat session not found.</response>
    [HttpGet("sessions/{id}/stream")]
    [EnableRateLimiting(RateLimitingPolicyNames.HotPathPerUser)]
    [Produces("text/event-stream")]
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
