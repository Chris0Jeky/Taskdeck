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
[Route("api/boards/{boardId}/columns")]
public class ColumnsController : AuthenticatedControllerBase
{
    private readonly ColumnService _columnService;
    private readonly BoardAuthorizationService _authorizationService;

    public ColumnsController(
        ColumnService columnService,
        BoardAuthorizationService authorizationService,
        IUserContext userContext)
        : base(userContext)
    {
        _columnService = columnService;
        _authorizationService = authorizationService;
    }

    [HttpGet]
    public async Task<IActionResult> GetColumns(Guid boardId)
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

        var result = await _columnService.GetColumnsByBoardIdAsync(boardId);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpPost]
    public async Task<IActionResult> CreateColumn(Guid boardId, [FromBody] CreateColumnDto dto)
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

        // Ensure boardId from route matches DTO
        var createDto = dto with { BoardId = boardId };
        var result = await _columnService.CreateColumnAsync(createDto);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetColumns), new { boardId }, result.Value)
            : result.ToErrorActionResult();
    }

    [HttpPatch("{columnId}")]
    public async Task<IActionResult> UpdateColumn(Guid boardId, Guid columnId, [FromBody] UpdateColumnDto dto)
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

        var result = await _columnService.UpdateColumnAsync(boardId, columnId, dto);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpDelete("{columnId}")]
    public async Task<IActionResult> DeleteColumn(Guid boardId, Guid columnId)
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

        var result = await _columnService.DeleteColumnAsync(boardId, columnId);
        return result.IsSuccess ? NoContent() : result.ToErrorActionResult();
    }

    [HttpPost("reorder")]
    public async Task<IActionResult> ReorderColumns(Guid boardId, [FromBody] ReorderColumnsDto dto)
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

        var result = await _columnService.ReorderColumnsAsync(boardId, dto);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }
}
