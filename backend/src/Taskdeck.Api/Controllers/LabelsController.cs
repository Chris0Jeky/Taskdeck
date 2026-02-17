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
[Route("api/boards/{boardId}/labels")]
public class LabelsController : AuthenticatedControllerBase
{
    private readonly LabelService _labelService;
    private readonly BoardAuthorizationService _authorizationService;

    public LabelsController(
        LabelService labelService,
        BoardAuthorizationService authorizationService,
        IUserContext userContext)
        : base(userContext)
    {
        _labelService = labelService;
        _authorizationService = authorizationService;
    }

    [HttpGet]
    public async Task<IActionResult> GetLabels(Guid boardId)
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

        var result = await _labelService.GetLabelsByBoardIdAsync(boardId);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpPost]
    public async Task<IActionResult> CreateLabel(Guid boardId, [FromBody] CreateLabelDto dto)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var permissionError = await EnsureBoardPermissionAsync(
            _authorizationService,
            userId,
            boardId,
            static (authorizationService, actorId, targetBoardId) => authorizationService.CanWriteBoardAsync(actorId, targetBoardId),
            "You do not have permission to modify this board");

        if (permissionError is not null)
            return permissionError;

        var createDto = dto with { BoardId = boardId };
        var result = await _labelService.CreateLabelAsync(createDto);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetLabels), new { boardId }, result.Value)
            : result.ToErrorActionResult();
    }

    [HttpPatch("{labelId}")]
    public async Task<IActionResult> UpdateLabel(Guid boardId, Guid labelId, [FromBody] UpdateLabelDto dto)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var permissionError = await EnsureBoardPermissionAsync(
            _authorizationService,
            userId,
            boardId,
            static (authorizationService, actorId, targetBoardId) => authorizationService.CanWriteBoardAsync(actorId, targetBoardId),
            "You do not have permission to modify this board");

        if (permissionError is not null)
            return permissionError;

        var result = await _labelService.UpdateLabelAsync(boardId, labelId, dto);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpDelete("{labelId}")]
    public async Task<IActionResult> DeleteLabel(Guid boardId, Guid labelId)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var permissionError = await EnsureBoardPermissionAsync(
            _authorizationService,
            userId,
            boardId,
            static (authorizationService, actorId, targetBoardId) => authorizationService.CanWriteBoardAsync(actorId, targetBoardId),
            "You do not have permission to modify this board");

        if (permissionError is not null)
            return permissionError;

        var result = await _labelService.DeleteLabelAsync(boardId, labelId);
        return result.IsSuccess ? NoContent() : result.ToErrorActionResult();
    }
}
