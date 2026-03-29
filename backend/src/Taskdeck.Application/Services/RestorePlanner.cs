using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

/// <summary>
/// Plans a restore operation: validates the archive item, checks permissions,
/// and resolves the target board. Returns a RestorePlan that the executor can act on.
/// </summary>
public class RestorePlanner
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuthorizationService? _authorizationService;

    public RestorePlanner(
        IUnitOfWork unitOfWork,
        IAuthorizationService? authorizationService = null)
    {
        _unitOfWork = unitOfWork;
        _authorizationService = authorizationService;
    }

    /// <summary>
    /// Validates an archive item for restore and returns a plan describing
    /// the item, target board, and entity type to restore.
    /// </summary>
    public async Task<Result<RestorePlan>> PlanRestoreAsync(
        Guid archiveItemId,
        RestoreArchiveItemDto dto,
        Guid restoredByUserId,
        CancellationToken cancellationToken = default)
    {
        // 1. Look up archive item
        var archiveItem = await _unitOfWork.ArchiveItems.GetByIdAsync(archiveItemId, cancellationToken);
        if (archiveItem == null)
            return Result.Failure<RestorePlan>(
                ErrorCodes.NotFound,
                $"Archive item with ID {archiveItemId} not found");

        if (archiveItem.RestoreStatus != RestoreStatus.Available)
            return Result.Failure<RestorePlan>(
                ErrorCodes.InvalidOperation,
                $"Cannot restore archive item with status {archiveItem.RestoreStatus}");

        // 2. Determine target board
        var targetBoardId = dto.TargetBoardId ?? archiveItem.BoardId;

        // 3. Check permissions
        if (_authorizationService != null)
        {
            var canWriteResult = await _authorizationService.CanWriteBoardAsync(restoredByUserId, targetBoardId);
            if (!canWriteResult.IsSuccess)
                return Result.Failure<RestorePlan>(canWriteResult.ErrorCode, canWriteResult.ErrorMessage);

            if (!canWriteResult.Value)
                return Result.Failure<RestorePlan>(
                    ErrorCodes.Forbidden,
                    "User does not have permission to restore to target board");
        }

        // 4. For non-board entities, validate the target board
        if (archiveItem.EntityType != "board")
        {
            var targetBoard = await _unitOfWork.Boards.GetByIdAsync(targetBoardId, cancellationToken);
            if (targetBoard == null)
                return Result.Failure<RestorePlan>(
                    ErrorCodes.NotFound,
                    $"Target board with ID {targetBoardId} not found");
            if (targetBoard.IsArchived)
                return Result.Failure<RestorePlan>(
                    ErrorCodes.InvalidOperation,
                    "Cannot restore to an archived board");
        }

        return Result.Success(new RestorePlan(archiveItem, targetBoardId, dto));
    }
}

/// <summary>
/// The validated plan for a restore operation, ready for execution.
/// </summary>
public record RestorePlan(
    ArchiveItem ArchiveItem,
    Guid TargetBoardId,
    RestoreArchiveItemDto Options);
