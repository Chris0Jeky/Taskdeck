using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/agents")]
public sealed class AgentsController : AuthenticatedControllerBase
{
    private readonly IAgentProfileService _agentProfileService;
    private readonly IAgentRunService _agentRunService;

    public AgentsController(
        IAgentProfileService agentProfileService,
        IAgentRunService agentRunService,
        IUserContext userContext)
        : base(userContext)
    {
        _agentProfileService = agentProfileService;
        _agentRunService = agentRunService;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _agentProfileService.ListAsync(userId, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAgentProfileDto dto, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _agentProfileService.CreateAsync(userId, dto, ct);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value)
            : result.ToErrorActionResult();
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _agentProfileService.GetByIdAsync(id, userId, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpPost("{id:guid}/runs")]
    public async Task<IActionResult> StartRun(Guid id, [FromBody] StartAgentRunDto dto, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _agentRunService.StartManualRunAsync(id, userId, dto, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
}
