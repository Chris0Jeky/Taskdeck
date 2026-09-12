using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Route("api/boards/{boardId:guid}/estimate-rollups")]
[Authorize]
public sealed class BoardEstimateRollupsController(IBoardEstimateRollupService service, IUserContext userContext)
    : AuthenticatedControllerBase(userContext)
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid boardId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult)) return errorResult!;
        var result = await service.GetAsync(boardId, userId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
}
