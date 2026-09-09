using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/workspace/plan")]
public class WorkspacePlanController : AuthenticatedControllerBase
{
    private readonly WorkspacePlanService service;
    public WorkspacePlanController(WorkspacePlanService service, IUserContext userContext) : base(userContext) { this.service = service; }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var actor, out var error)) return error!;
        var result = await service.GetAsync(actor, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpPut]
    public async Task<IActionResult> Save([FromBody] SaveWorkspacePlanDto dto, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var actor, out var error)) return error!;
        var result = await service.SaveAsync(actor, dto, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpPost("focus")]
    public async Task<IActionResult> Focus([FromBody] FocusWorkspacePlanDto dto, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var actor, out var error)) return error!;
        var result = await service.FocusAsync(actor, dto, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
}
