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
[Route("api/boards/{boardId}/cards/{cardId}/comments")]
public class CardCommentsController : AuthenticatedControllerBase
{
    private readonly CardCommentService _cardCommentService;
    private readonly BoardAuthorizationService _authorizationService;

    public CardCommentsController(
        CardCommentService cardCommentService,
        BoardAuthorizationService authorizationService,
        IUserContext userContext)
        : base(userContext)
    {
        _cardCommentService = cardCommentService;
        _authorizationService = authorizationService;
    }

    [HttpGet]
    public async Task<IActionResult> GetComments(
        Guid boardId,
        Guid cardId,
        CancellationToken cancellationToken = default)
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

        var result = await _cardCommentService.GetCommentsAsync(boardId, cardId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpPost]
    public async Task<IActionResult> CreateComment(
        Guid boardId,
        Guid cardId,
        [FromBody] CreateCardCommentDto dto,
        CancellationToken cancellationToken = default)
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

        var result = await _cardCommentService.CreateCommentAsync(boardId, cardId, userId, dto, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpPatch("{commentId}")]
    public async Task<IActionResult> UpdateComment(
        Guid boardId,
        Guid cardId,
        Guid commentId,
        [FromBody] UpdateCardCommentDto dto,
        CancellationToken cancellationToken = default)
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

        var result = await _cardCommentService.UpdateCommentAsync(boardId, cardId, commentId, userId, dto, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.ToErrorActionResult();
    }

    [HttpDelete("{commentId}")]
    public async Task<IActionResult> DeleteComment(
        Guid boardId,
        Guid cardId,
        Guid commentId,
        CancellationToken cancellationToken = default)
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

        var result = await _cardCommentService.DeleteCommentAsync(boardId, cardId, commentId, userId, cancellationToken);
        return result.IsSuccess ? NoContent() : result.ToErrorActionResult();
    }
}
