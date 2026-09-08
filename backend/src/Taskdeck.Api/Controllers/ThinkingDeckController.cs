using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/boards/{boardId}/cards/{cardId}/thinking")]
public sealed class ThinkingDeckController(ThinkingDeckService service, IUserContext userContext) : AuthenticatedControllerBase(userContext)
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid boardId, Guid cardId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId, out var error)) return error!;
        var result = await service.GetAsync(userId, boardId, cardId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpPut]
    [RequestSizeLimit(200000)]
    public async Task<IActionResult> Save(Guid boardId, Guid cardId, SaveThinkingDeckDto dto, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId, out var error)) return error!;
        var result = await service.SaveAsync(userId, boardId, cardId, dto, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
}
