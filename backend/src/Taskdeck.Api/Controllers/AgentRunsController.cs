using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/agents/{agentId}/runs")]
public class AgentRunsController : AuthenticatedControllerBase
{
    private readonly AgentRunService _agentRunService;

    public AgentRunsController(
        AgentRunService agentRunService,
        IUserContext userContext)
        : base(userContext)
    {
        _agentRunService = agentRunService;
    }

    [HttpPost]
    public async Task<IActionResult> CreateRun(
        Guid agentId,
        [FromBody] CreateAgentRunDto dto,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _agentRunService.CreateRunAsync(agentId, userId, dto, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetRun), new { agentId, runId = result.Value.Id }, result.Value)
            : result.ToErrorActionResult();
    }

    [HttpGet]
    public async Task<IActionResult> ListRuns(
        Guid agentId,
        [FromQuery] int limit = 100, // capped to 500 below
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var boundedLimit = Math.Clamp(limit, 1, 500);
        var result = await _agentRunService.GetRunsForProfileAsync(agentId, userId, boundedLimit, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpGet("{runId}")]
    public async Task<IActionResult> GetRun(
        Guid agentId,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _agentRunService.GetRunWithEventsAsync(agentId, runId, userId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
}
