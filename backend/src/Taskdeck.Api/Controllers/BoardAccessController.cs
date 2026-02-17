using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using BoardAuthorizationService = Taskdeck.Application.Services.IAuthorizationService;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/boards/{boardId}/access")]
public class BoardAccessController : AuthenticatedControllerBase
{
    private readonly BoardAccessService _boardAccessService;
    private readonly BoardAuthorizationService _authorizationService;

    public BoardAccessController(
        BoardAccessService boardAccessService,
        BoardAuthorizationService authorizationService,
        IUserContext userContext)
        : base(userContext)
    {
        _boardAccessService = boardAccessService;
        _authorizationService = authorizationService;
    }

    [HttpGet]
    public async Task<IActionResult> GetBoardAccess(Guid boardId)
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

        var result = await _boardAccessService.GetBoardAccessListAsync(boardId);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpPost]
    public async Task<IActionResult> GrantAccess(Guid boardId, [FromBody] GrantAccessDto dto)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var dtoWithBoardId = dto with { BoardId = boardId };
        var result = await _boardAccessService.GrantAccessAsync(dtoWithBoardId, userId);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpPut("{accessId}")]
    public async Task<IActionResult> UpdateAccess(Guid boardId, Guid accessId, [FromBody] UpdateAccessDto dto)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _boardAccessService.UpdateAccessAsync(boardId, accessId, dto, userId);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpDelete("{accessId}")]
    public async Task<IActionResult> RevokeAccess(Guid boardId, Guid accessId)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var result = await _boardAccessService.RevokeAccessAsync(boardId, accessId, userId);
        return result.IsSuccess ? NoContent() : result.ToErrorActionResult();
    }
}
