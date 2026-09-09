using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/boards/{boardId}/cards/{cardId}/thinking/steps/{layerId}/{itemId}/card")]
public class ThinkingStepController : AuthenticatedControllerBase
{
    private readonly ThinkingStepService service;
    public ThinkingStepController(ThinkingStepService service, IUserContext userContext) : base(userContext)
    { this.service = service; }

    [HttpPost]
    [RequestSizeLimit(4000)]
    public async Task<IActionResult> Promote(Guid boardId, Guid cardId, Guid layerId, Guid itemId,
        PromoteThinkingStepDto dto, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId, out var error)) return error!;
        var result = await service.PromoteAsync(userId, boardId, cardId, layerId, itemId, dto, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
}
