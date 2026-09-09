using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/boards/{boardId:guid}/cards/{cardId:guid}/thinking/questions/{layerId:guid}/answer")]
public class ThinkingAnswerController : AuthenticatedControllerBase
{
    private readonly ThinkingAnswerService service;
    public ThinkingAnswerController(ThinkingAnswerService service, IUserContext userContext) : base(userContext)
    { this.service = service; }
    [HttpGet]
    public async Task<IActionResult> Get(Guid boardId, Guid cardId, Guid layerId, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId, out var error)) return error!;
        var result = await service.GetAsync(userId, boardId, cardId, layerId, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
    [HttpPost]
    public async Task<IActionResult> Answer(Guid boardId, Guid cardId, Guid layerId, ThinkingAnswerDto dto, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId, out var error)) return error!;
        var result = await service.AnswerAsync(userId, boardId, cardId, layerId, dto, ct);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
}
