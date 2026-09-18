using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Route("api/boards/{boardId:guid}/relations")]
[Authorize]
public class BoardCardRelationsController : AuthenticatedControllerBase
{
    private readonly IBoardRelationService _service;

    public BoardCardRelationsController(IBoardRelationService service, IUserContext userContext)
        : base(userContext)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Get(Guid boardId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult)) return errorResult!;
        var result = await _service.GetAsync(userId, boardId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
}
