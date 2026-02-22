using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;
using BoardAuthorizationService = Taskdeck.Application.Services.IAuthorizationService;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/logs")]
public class LogsController : AuthenticatedControllerBase
{
    private readonly ILogQueryService _logQueryService;
    private readonly BoardAuthorizationService _authorizationService;

    public LogsController(
        ILogQueryService logQueryService,
        BoardAuthorizationService authorizationService,
        IUserContext userContext) : base(userContext)
    {
        _logQueryService = logQueryService;
        _authorizationService = authorizationService;
    }

    [HttpGet]
    public async Task<IActionResult> QueryLogs(
        [FromQuery] string? level,
        [FromQuery] string? source,
        [FromQuery] Guid? userId,
        [FromQuery] Guid? boardId,
        [FromQuery] string? correlationId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        if (!TryGetCurrentUserId(out var callerUserId, out var errorResult))
            return errorResult!;

        if (userId.HasValue && userId.Value != callerUserId)
        {
            return Result.Failure(
                ErrorCodes.Forbidden,
                "You can only query logs for your own user.").ToErrorActionResult();
        }

        if (boardId.HasValue)
        {
            var permissionError = await EnsureBoardPermissionAsync(
                _authorizationService,
                callerUserId,
                boardId.Value,
                static (authorizationService, actorId, targetBoardId) =>
                    authorizationService.CanReadBoardAsync(actorId, targetBoardId),
                "You do not have permission to view logs for this board");

            if (permissionError is not null)
                return permissionError;
        }

        var query = new LogQueryDto(level, source, callerUserId, boardId, correlationId, from, to, limit);
        var result = await _logQueryService.QueryLogsAsync(query, ct);
        if (!result.IsSuccess)
            return result.ToErrorActionResult();

        var scopedEntries = result.Value.ToList();
        if (!string.IsNullOrWhiteSpace(correlationId) && scopedEntries.Count == 0)
        {
            var correlationExistsResult = await _logQueryService.QueryLogsAsync(
                new LogQueryDto(CorrelationId: correlationId, Limit: 1),
                ct);

            if (!correlationExistsResult.IsSuccess)
                return correlationExistsResult.ToErrorActionResult();

            if (correlationExistsResult.Value.Any())
            {
                return Result.Failure(
                    ErrorCodes.Forbidden,
                    "You do not have permission to access logs for this correlation.").ToErrorActionResult();
            }
        }

        return Ok(scopedEntries);
    }

    [HttpGet("stream")]
    public async Task<IActionResult> GetStream(
        [FromQuery] string? level,
        [FromQuery] string? source,
        CancellationToken ct = default)
    {
        if (!TryGetCurrentUserId(out var callerUserId, out var errorResult))
            return errorResult!;

        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        var filter = new LogQueryDto(Level: level, Source: source, UserId: callerUserId);

        await foreach (var streamEvent in _logQueryService.StreamLogsAsync(filter, ct))
        {
            var data = System.Text.Json.JsonSerializer.Serialize(streamEvent);
            await Response.WriteAsync($"event: {streamEvent.EventType}\ndata: {data}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }

        return new EmptyResult();
    }

    [HttpGet("correlation/{correlationId}")]
    public async Task<IActionResult> GetByCorrelationId(string correlationId, CancellationToken ct = default)
    {
        if (!TryGetCurrentUserId(out var callerUserId, out var errorResult))
            return errorResult!;

        var scopedResult = await _logQueryService.QueryLogsAsync(
            new LogQueryDto(CorrelationId: correlationId, UserId: callerUserId, Limit: 500),
            ct);
        if (!scopedResult.IsSuccess)
            return scopedResult.ToErrorActionResult();

        var scopedEntries = scopedResult.Value.ToList();
        if (scopedEntries.Count > 0)
            return Ok(scopedEntries);

        var anyResult = await _logQueryService.QueryLogsAsync(
            new LogQueryDto(CorrelationId: correlationId, Limit: 1),
            ct);
        if (!anyResult.IsSuccess)
            return anyResult.ToErrorActionResult();

        if (anyResult.Value.Any())
        {
            return Result.Failure(
                ErrorCodes.Forbidden,
                "You do not have permission to access logs for this correlation.").ToErrorActionResult();
        }

        return Result.Failure<IEnumerable<LogEntryDto>>(
            ErrorCodes.NotFound,
            $"No log entries found for correlation ID '{correlationId}'").ToErrorActionResult();
    }
}
