using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using BoardAuthorizationService = Taskdeck.Application.Services.IAuthorizationService;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class AuditController : AuthenticatedControllerBase
{
    private readonly HistoryService _historyService;
    private readonly BoardAuthorizationService _authorizationService;

    public AuditController(
        HistoryService historyService,
        BoardAuthorizationService authorizationService,
        IUserContext userContext)
        : base(userContext)
    {
        _historyService = historyService;
        _authorizationService = authorizationService;
    }

    [HttpGet("boards/{boardId}")]
    public async Task<IActionResult> GetBoardHistory(Guid boardId, [FromQuery] int limit = 100)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var permissionError = await EnsureBoardPermissionAsync(
            _authorizationService,
            userId,
            boardId,
            static (authorizationService, actorId, targetBoardId) => authorizationService.CanReadBoardAsync(actorId, targetBoardId),
            "You do not have access to this board");

        if (permissionError is not null)
            return permissionError;

        var result = await _historyService.GetBoardHistoryAsync(boardId, limit);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpGet("entities/{entityType}/{entityId}")]
    public async Task<IActionResult> GetEntityHistory(string entityType, Guid entityId, [FromQuery] int limit = 100)
    {
        if (!TryGetCurrentUserId(out _, out var errorResult))
            return errorResult!;

        var result = await _historyService.GetEntityHistoryAsync(entityType, entityId, limit);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpGet("users/me")]
    public async Task<IActionResult> GetUserHistory([FromQuery] int limit = 100)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _historyService.GetUserHistoryAsync(userId, limit);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
}
