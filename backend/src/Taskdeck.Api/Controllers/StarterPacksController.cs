using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;
using BoardAuthorizationService = Taskdeck.Application.Services.IAuthorizationService;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/boards/{boardId}/starter-packs")]
public class StarterPacksController : AuthenticatedControllerBase
{
    private readonly IStarterPackApplyService _starterPackApplyService;
    private readonly BoardAuthorizationService _authorizationService;

    public StarterPacksController(
        IStarterPackApplyService starterPackApplyService,
        BoardAuthorizationService authorizationService,
        IUserContext userContext)
        : base(userContext)
    {
        _starterPackApplyService = starterPackApplyService;
        _authorizationService = authorizationService;
    }

    [HttpPost("apply")]
    public async Task<IActionResult> ApplyStarterPack(
        Guid boardId,
        [FromBody] ApplyStarterPackDto? dto,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
        {
            return errorResult!;
        }

        var permissionError = await EnsureBoardPermissionAsync(
            _authorizationService,
            userId,
            boardId,
            static (authorizationService, actorId, targetBoardId) => authorizationService.CanWriteBoardAsync(actorId, targetBoardId),
            "You do not have permission to modify this board");

        if (permissionError is not null)
        {
            return permissionError;
        }

        if (dto == null)
        {
            return Result.Failure(ErrorCodes.ValidationError, "Request body is required.").ToErrorActionResult();
        }

        var result = await _starterPackApplyService.ApplyToBoardAsync(boardId, dto, cancellationToken);
        if (!result.IsSuccess)
        {
            return result.ToErrorActionResult();
        }

        if (!dto.DryRun && result.Value.HasConflicts)
        {
            return Conflict(result.Value);
        }

        return Ok(result.Value);
    }
}
