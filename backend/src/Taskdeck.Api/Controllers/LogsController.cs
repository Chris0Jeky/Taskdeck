using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/logs")]
public class LogsController : ControllerBase
{
    private readonly ILogQueryService _logQueryService;

    public LogsController(ILogQueryService logQueryService)
    {
        _logQueryService = logQueryService;
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
        var query = new LogQueryDto(level, source, userId, boardId, correlationId, from, to, limit);
        var result = await _logQueryService.QueryLogsAsync(query, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpGet("stream")]
    public async Task GetStream(
        [FromQuery] string? level,
        [FromQuery] string? source,
        CancellationToken ct = default)
    {
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        var filter = new LogQueryDto(Level: level, Source: source);

        await foreach (var streamEvent in _logQueryService.StreamLogsAsync(filter, ct))
        {
            var data = System.Text.Json.JsonSerializer.Serialize(streamEvent);
            await Response.WriteAsync($"event: {streamEvent.EventType}\ndata: {data}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
    }

    [HttpGet("correlation/{correlationId}")]
    public async Task<IActionResult> GetByCorrelationId(string correlationId, CancellationToken ct = default)
    {
        var result = await _logQueryService.GetByCorrelationIdAsync(correlationId, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
}
