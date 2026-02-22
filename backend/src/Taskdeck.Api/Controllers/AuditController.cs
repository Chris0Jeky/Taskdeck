using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Domain.Common;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Exceptions;
using BoardAuthorizationService = Taskdeck.Application.Services.IAuthorizationService;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class AuditController : AuthenticatedControllerBase
{
    private readonly HistoryService _historyService;
    private readonly BoardAuthorizationService _authorizationService;
    private readonly IUnitOfWork _unitOfWork;

    public AuditController(
        HistoryService historyService,
        BoardAuthorizationService authorizationService,
        IUnitOfWork unitOfWork,
        IUserContext userContext)
        : base(userContext)
    {
        _historyService = historyService;
        _authorizationService = authorizationService;
        _unitOfWork = unitOfWork;
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
    public async Task<IActionResult> GetEntityHistory(
        string entityType,
        Guid entityId,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var scopeResult = await ResolveEntityScopeAsync(entityType, entityId, cancellationToken);
        if (!scopeResult.IsSuccess)
            return scopeResult.ToErrorActionResult();

        var permissionError = await EnsureBoardPermissionAsync(
            _authorizationService,
            userId,
            scopeResult.Value.BoardId,
            static (authorizationService, actorId, targetBoardId) => authorizationService.CanReadBoardAsync(actorId, targetBoardId),
            "You do not have access to this entity");

        if (permissionError is not null)
            return permissionError;

        var result = await _historyService.GetEntityHistoryAsync(scopeResult.Value.CanonicalEntityType, entityId, limit);
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

    private async Task<Result<EntityAuditScope>> ResolveEntityScopeAsync(
        string entityType,
        Guid entityId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(entityType))
        {
            return Result.Failure<EntityAuditScope>(
                ErrorCodes.ValidationError,
                "Entity type cannot be empty");
        }

        if (entityId == Guid.Empty)
        {
            return Result.Failure<EntityAuditScope>(
                ErrorCodes.ValidationError,
                "Entity ID cannot be empty");
        }

        var normalizedType = entityType.Trim().ToLowerInvariant();
        switch (normalizedType)
        {
            case "board":
            {
                var board = await _unitOfWork.Boards.GetByIdAsync(entityId, cancellationToken);
                if (board is null)
                {
                    return Result.Failure<EntityAuditScope>(
                        ErrorCodes.NotFound,
                        $"Board with ID {entityId} not found");
                }

                return Result.Success(new EntityAuditScope("Board", board.Id));
            }
            case "column":
            {
                var column = await _unitOfWork.Columns.GetByIdAsync(entityId, cancellationToken);
                if (column is null)
                {
                    return Result.Failure<EntityAuditScope>(
                        ErrorCodes.NotFound,
                        $"Column with ID {entityId} not found");
                }

                return Result.Success(new EntityAuditScope("Column", column.BoardId));
            }
            case "card":
            {
                var card = await _unitOfWork.Cards.GetByIdAsync(entityId, cancellationToken);
                if (card is null)
                {
                    return Result.Failure<EntityAuditScope>(
                        ErrorCodes.NotFound,
                        $"Card with ID {entityId} not found");
                }

                return Result.Success(new EntityAuditScope("Card", card.BoardId));
            }
            case "label":
            {
                var label = await _unitOfWork.Labels.GetByIdAsync(entityId, cancellationToken);
                if (label is null)
                {
                    return Result.Failure<EntityAuditScope>(
                        ErrorCodes.NotFound,
                        $"Label with ID {entityId} not found");
                }

                return Result.Success(new EntityAuditScope("Label", label.BoardId));
            }
            default:
                return Result.Failure<EntityAuditScope>(
                    ErrorCodes.ValidationError,
                    "Entity type must be one of: Board, Column, Card, Label");
        }
    }

    private sealed record EntityAuditScope(string CanonicalEntityType, Guid BoardId);
}
