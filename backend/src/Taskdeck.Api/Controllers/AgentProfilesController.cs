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
public class AgentProfilesController : AuthenticatedControllerBase
{
    private readonly AgentProfileService _agentProfileService;

    public AgentProfilesController(
        AgentProfileService agentProfileService,
        IUserContext userContext)
        : base(userContext)
    {
        _agentProfileService = agentProfileService;
    }

    [HttpGet]
    public async Task<IActionResult> ListProfiles(CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _agentProfileService.GetByUserIdAsync(userId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetProfile(Guid id, CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _agentProfileService.GetByIdAsync(id, userId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpPost]
    public async Task<IActionResult> CreateProfile(
        [FromBody] CreateAgentProfileDto dto,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _agentProfileService.CreateAsync(userId, dto, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetProfile), new { id = result.Value.Id }, result.Value)
            : result.ToErrorActionResult();
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateProfile(
        Guid id,
        [FromBody] UpdateAgentProfileDto dto,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _agentProfileService.UpdateAsync(id, userId, dto, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteProfile(Guid id, CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _agentProfileService.DeleteAsync(id, userId, cancellationToken);
        return result.IsSuccess ? NoContent() : result.ToErrorActionResult();
    }
}
