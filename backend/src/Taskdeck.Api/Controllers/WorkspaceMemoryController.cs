using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/workspace-memory")]
public class WorkspaceMemoryController : AuthenticatedControllerBase
{
    private readonly WorkspaceInsightService service;
    public WorkspaceMemoryController(WorkspaceInsightService service, IUserContext userContext) : base(userContext)
    { this.service = service; }
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid boardId, [FromQuery] bool archived, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var user, out var error)) return error!;
        var result = await service.MemoriesAsync(user, boardId, archived, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
    [HttpPost]
    public async Task<IActionResult> Create(CreateWorkspaceMemoryDto dto, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var user, out var error)) return error!;
        var result = await service.CreateAsync(user, dto, ct);
        return result.IsSuccess ? Created($"/api/workspace-memory?boardId={dto.BoardId}", result.Value) : result.ToErrorActionResult();
    }
    [HttpGet("{id:guid}/sources")]
    public async Task<IActionResult> Sources(Guid id, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var user, out var error)) return error!;
        var result = await service.SourcesAsync(user, id, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateWorkspaceMemoryDto dto, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var user, out var error)) return error!;
        var result = await service.UpdateAsync(user, id, dto, null, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Archive(Guid id, ArchiveWorkspaceMemoryDto dto, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var user, out var error)) return error!;
        var result = await service.UpdateAsync(user, id, null, dto, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
}
