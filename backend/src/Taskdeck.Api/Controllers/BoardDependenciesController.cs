using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/boards/{boardId}/dependencies")]
public class BoardDependenciesController : AuthenticatedControllerBase
{
    private readonly BoardDependencyService service;
    public BoardDependenciesController(BoardDependencyService service, IUserContext userContext) : base(userContext)
    { this.service = service; }
    [HttpGet]
    public async Task<IActionResult> Get(Guid boardId, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var actor, out var error)) return error!;
        var result = await service.GetAsync(actor, boardId, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
    [HttpPut]
    [RequestSizeLimit(100000)]
    public async Task<IActionResult> Save(Guid boardId, SaveBoardDependenciesDto dto, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var actor, out var error)) return error!;
        var result = await service.SaveAsync(actor, boardId, dto, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
}
