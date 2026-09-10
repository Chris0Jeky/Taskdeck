using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

[ApiController, Authorize, Route("api/workspace-attention")]
public sealed class WorkspaceAttentionController(WorkspaceAttentionService service, IUserContext context) : AuthenticatedControllerBase(context)
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var user, out var error)) return error!;
        return Ok(await service.GetAsync(user, ct));
    }
    [HttpPut]
    public async Task<IActionResult> Save(SaveWorkspaceAttentionDto dto, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var user, out var error)) return error!;
        var result = await service.SaveAsync(user, dto, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
    [HttpPost("claim")]
    public async Task<IActionResult> Claim(ClaimWorkspaceAttentionDto dto, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var user, out var error)) return error!;
        var result = await service.ClaimAsync(user, dto.BoardId, ct);
        return !result.IsSuccess ? result.ToErrorActionResult() : result.Value is null ? NoContent() : Ok(result.Value);
    }
}
