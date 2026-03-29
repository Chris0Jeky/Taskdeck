using System.Text.Json;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

/// <summary>
/// Executes restore operations for individual entity types (board, column, card).
/// Handles snapshot deserialization, conflict resolution, and entity creation.
/// </summary>
public class RestoreExecutor
{
    private readonly IUnitOfWork _unitOfWork;

    public RestoreExecutor(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Dispatches a restore to the appropriate entity-type handler.
    /// </summary>
    public Task<Result<RestoreResult>> ExecuteAsync(
        RestorePlan plan,
        Guid restoredByUserId,
        CancellationToken cancellationToken = default)
    {
        return plan.ArchiveItem.EntityType switch
        {
            "board" => RestoreBoardAsync(plan.ArchiveItem, plan.Options, restoredByUserId, cancellationToken),
            "column" => RestoreColumnAsync(plan.ArchiveItem, plan.TargetBoardId, plan.Options, restoredByUserId, cancellationToken),
            "card" => RestoreCardAsync(plan.ArchiveItem, plan.TargetBoardId, plan.Options, restoredByUserId, cancellationToken),
            _ => Task.FromResult(Result.Failure<RestoreResult>(
                ErrorCodes.ValidationError,
                $"Unknown entity type: {plan.ArchiveItem.EntityType}"))
        };
    }

    internal async Task<Result<RestoreResult>> RestoreBoardAsync(
        ArchiveItem archiveItem,
        RestoreArchiveItemDto dto,
        Guid restoredByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = JsonSerializer.Deserialize<BoardSnapshot>(archiveItem.SnapshotJson);
            if (snapshot == null)
                return Result.Failure<RestoreResult>(
                    ErrorCodes.ValidationError,
                    "Failed to deserialize board snapshot");

            var existingBoards = await _unitOfWork.Boards.SearchAsync(snapshot.Name, includeArchived: false, cancellationToken);
            var conflictExists = existingBoards.Any(b => b.Name == snapshot.Name);

            var nameResult = ArchiveConflictDetector.ResolveName(
                snapshot.Name, conflictExists, dto.ConflictStrategy, "board");
            if (!nameResult.IsSuccess)
                return Result.Failure<RestoreResult>(nameResult.ErrorCode, nameResult.ErrorMessage);
            var resolvedName = nameResult.Value;

            if (dto.RestoreMode == RestoreMode.InPlace)
            {
                var existingBoard = await _unitOfWork.Boards.GetByIdAsync(archiveItem.EntityId, cancellationToken);
                if (existingBoard != null && existingBoard.IsArchived)
                {
                    existingBoard.Unarchive();
                    existingBoard.Update(resolvedName, snapshot.Description);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);

                    return Result.Success(new RestoreResult(
                        true,
                        existingBoard.Id,
                        null,
                        resolvedName));
                }
            }

            var newBoard = new Board(resolvedName, snapshot.Description, restoredByUserId);
            await _unitOfWork.Boards.AddAsync(newBoard, cancellationToken);

            return Result.Success(new RestoreResult(
                true,
                newBoard.Id,
                null,
                resolvedName));
        }
        catch (JsonException ex)
        {
            return Result.Failure<RestoreResult>(
                ErrorCodes.ValidationError,
                $"Invalid snapshot format: {ex.Message}");
        }
    }

    internal async Task<Result<RestoreResult>> RestoreColumnAsync(
        ArchiveItem archiveItem,
        Guid targetBoardId,
        RestoreArchiveItemDto dto,
        Guid restoredByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = JsonSerializer.Deserialize<ColumnSnapshot>(archiveItem.SnapshotJson);
            if (snapshot == null)
                return Result.Failure<RestoreResult>(
                    ErrorCodes.ValidationError,
                    "Failed to deserialize column snapshot");

            var board = await _unitOfWork.Boards.GetByIdWithDetailsAsync(targetBoardId, cancellationToken);
            if (board == null)
                return Result.Failure<RestoreResult>(ErrorCodes.NotFound, $"Board with ID {targetBoardId} not found");

            var conflictExists = board.Columns.Any(c => c.Name == snapshot.Name);

            var nameResult = ArchiveConflictDetector.ResolveName(
                snapshot.Name, conflictExists, dto.ConflictStrategy, "column");
            if (!nameResult.IsSuccess)
                return Result.Failure<RestoreResult>(nameResult.ErrorCode, nameResult.ErrorMessage);
            var resolvedName = nameResult.Value;

            var maxPosition = board.Columns.Any() ? board.Columns.Max(c => c.Position) : -1;
            var newPosition = maxPosition + 1;

            var newColumn = new Column(targetBoardId, resolvedName, newPosition, snapshot.WipLimit);
            await _unitOfWork.Columns.AddAsync(newColumn, cancellationToken);

            return Result.Success(new RestoreResult(
                true,
                newColumn.Id,
                null,
                resolvedName));
        }
        catch (JsonException ex)
        {
            return Result.Failure<RestoreResult>(
                ErrorCodes.ValidationError,
                $"Invalid snapshot format: {ex.Message}");
        }
    }

    internal async Task<Result<RestoreResult>> RestoreCardAsync(
        ArchiveItem archiveItem,
        Guid targetBoardId,
        RestoreArchiveItemDto dto,
        Guid restoredByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = JsonSerializer.Deserialize<CardSnapshot>(archiveItem.SnapshotJson);
            if (snapshot == null)
                return Result.Failure<RestoreResult>(
                    ErrorCodes.ValidationError,
                    "Failed to deserialize card snapshot");

            var board = await _unitOfWork.Boards.GetByIdWithDetailsAsync(targetBoardId, cancellationToken);
            if (board == null)
                return Result.Failure<RestoreResult>(ErrorCodes.NotFound, $"Board with ID {targetBoardId} not found");

            Column? targetColumn = null;
            if (snapshot.ColumnId != Guid.Empty)
            {
                targetColumn = board.Columns.FirstOrDefault(c => c.Id == snapshot.ColumnId);
            }

            if (targetColumn == null)
            {
                targetColumn = board.Columns.OrderBy(c => c.Position).FirstOrDefault();
                if (targetColumn == null)
                    return Result.Failure<RestoreResult>(
                        ErrorCodes.InvalidOperation,
                        "Target board has no columns to restore card to");
            }

            var columnWithCards = await _unitOfWork.Columns.GetByIdWithCardsAsync(targetColumn.Id, cancellationToken);
            if (columnWithCards == null)
                return Result.Failure<RestoreResult>(ErrorCodes.NotFound, $"Column with ID {targetColumn.Id} not found");

            if (columnWithCards.WouldExceedWipLimitIfAdded())
                return Result.Failure<RestoreResult>(
                    ErrorCodes.WipLimitExceeded,
                    $"Cannot restore card, column '{columnWithCards.Name}' has reached its WIP limit");

            var existingCards = columnWithCards.Cards.ToList();
            var conflictExists = existingCards.Any(c => c.Title == snapshot.Title);

            var nameResult = ArchiveConflictDetector.ResolveName(
                snapshot.Title, conflictExists, dto.ConflictStrategy, "card");
            if (!nameResult.IsSuccess)
                return Result.Failure<RestoreResult>(nameResult.ErrorCode, nameResult.ErrorMessage);
            var resolvedTitle = nameResult.Value;

            var maxPosition = existingCards.Any() ? existingCards.Max(c => c.Position) : -1;
            var newPosition = maxPosition + 1;

            var newCard = new Card(
                targetBoardId,
                columnWithCards.Id,
                resolvedTitle,
                snapshot.Description,
                snapshot.DueDate,
                newPosition);

            if (snapshot.IsBlocked && !string.IsNullOrEmpty(snapshot.BlockReason))
            {
                newCard.Block(snapshot.BlockReason);
            }

            await _unitOfWork.Cards.AddAsync(newCard, cancellationToken);

            return Result.Success(new RestoreResult(
                true,
                newCard.Id,
                null,
                resolvedTitle));
        }
        catch (JsonException ex)
        {
            return Result.Failure<RestoreResult>(
                ErrorCodes.ValidationError,
                $"Invalid snapshot format: {ex.Message}");
        }
    }
}
