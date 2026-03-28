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
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpGet]
    public async Task<IActionResult> ListRuns(
        Guid agentId,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _agentRunService.GetRunsForProfileAsync(agentId, userId, limit, cancellationToken);
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
