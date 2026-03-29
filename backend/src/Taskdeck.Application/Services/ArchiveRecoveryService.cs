using System.Text.Json;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class ArchiveRecoveryService : IArchiveRecoveryService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuthorizationService? _authorizationService;

    public ArchiveRecoveryService(
        IUnitOfWork unitOfWork,
        IAuthorizationService? authorizationService = null)
    {
        _unitOfWork = unitOfWork;
        _authorizationService = authorizationService;
    }

    public async Task<Result<ArchiveItemDto>> CreateArchiveItemAsync(
        CreateArchiveItemDto dto,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var archiveItem = new ArchiveItem(
                dto.EntityType,
                dto.EntityId,
                dto.BoardId,
                dto.Name,
                dto.ArchivedByUserId,
                dto.SnapshotJson,
                dto.Reason);

            await _unitOfWork.ArchiveItems.AddAsync(archiveItem, cancellationToken);

            // Create audit log
            var auditLog = new AuditLog(
                "ArchiveItem",
                archiveItem.Id,
                AuditAction.Created,
                dto.ArchivedByUserId,
                $"Archived {dto.EntityType} '{dto.Name}' (ID: {dto.EntityId})");
            await _unitOfWork.AuditLogs.AddAsync(auditLog, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(MapToDto(archiveItem));
        }
        catch (DomainException ex)
        {
            return Result.Failure<ArchiveItemDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<IEnumerable<ArchiveItemDto>>> GetArchiveItemsAsync(
        string? entityType = null,
        Guid? boardId = null,
        RestoreStatus? status = null,
        int limit = 100,
        Guid? actingUserId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (actingUserId.HasValue && actingUserId.Value == Guid.Empty)
            {
                return Result.Failure<IEnumerable<ArchiveItemDto>>(
                    ErrorCodes.ValidationError,
                    "Acting user ID cannot be empty");
            }

            if (limit <= 0 || limit > 1000)
            {
                return Result.Failure<IEnumerable<ArchiveItemDto>>(
                    ErrorCodes.ValidationError,
                    "Limit must be between 1 and 1000");
            }

            if (!string.IsNullOrWhiteSpace(entityType))
            {
                entityType = entityType.Trim().ToLowerInvariant();
                if (entityType != "board" && entityType != "column" && entityType != "card")
                {
                    return Result.Failure<IEnumerable<ArchiveItemDto>>(
                        ErrorCodes.ValidationError,
                        "EntityType must be 'board', 'column', or 'card'");
                }
            }

            if (boardId.HasValue && _authorizationService is not null && actingUserId.HasValue)
            {
                var boardReadPermission = await _authorizationService.CanReadBoardAsync(
                    actingUserId.Value,
                    boardId.Value);

                if (!boardReadPermission.IsSuccess)
                {
                    return Result.Failure<IEnumerable<ArchiveItemDto>>(
                        boardReadPermission.ErrorCode,
                        boardReadPermission.ErrorMessage);
                }

                if (!boardReadPermission.Value)
                {
                    return Result.Failure<IEnumerable<ArchiveItemDto>>(
                        ErrorCodes.Forbidden,
                    "You do not have access to archive items for this board");
                }
            }

            if (_authorizationService is not null && actingUserId.HasValue && !boardId.HasValue)
            {
                return await GetArchiveItemsWithDeferredLimitAsync(
                    entityType,
                    status,
                    limit,
                    actingUserId.Value,
                    cancellationToken);
            }

            IEnumerable<ArchiveItem> items;

            if (entityType != null && boardId != null && status != null)
            {
                // Combined filter - need to implement custom query
                var allItems = await _unitOfWork.ArchiveItems.GetAllAsync(cancellationToken);
                items = allItems
                    .Where(i => i.EntityType == entityType 
                        && i.BoardId == boardId 
                        && i.RestoreStatus == status.Value)
                    .Take(limit);
            }
            else if (entityType != null)
            {
                items = await _unitOfWork.ArchiveItems.GetByEntityTypeAsync(entityType, limit, cancellationToken);
            }
            else if (boardId != null)
            {
                items = await _unitOfWork.ArchiveItems.GetByBoardIdAsync(boardId.Value, limit, cancellationToken);
            }
            else if (status != null)
            {
                items = await _unitOfWork.ArchiveItems.GetByStatusAsync(status.Value, limit, cancellationToken);
            }
            else
            {
                var allItems = await _unitOfWork.ArchiveItems.GetAllAsync(cancellationToken);
                items = allItems.Take(limit);
            }

            // Apply additional filters if needed
            if (entityType != null && boardId == null && status != null)
            {
                items = items.Where(i => i.RestoreStatus == status.Value);
            }
            else if (entityType == null && boardId != null && status != null)
            {
                items = items.Where(i => i.RestoreStatus == status.Value);
            }
            else if (entityType != null && boardId != null && status == null)
            {
                items = items.Where(i => i.BoardId == boardId.Value);
            }

            return Result.Success(items.Select(MapToDto));
        }
        catch (Exception ex)
        {
            return Result.Failure<IEnumerable<ArchiveItemDto>>(
                ErrorCodes.UnexpectedError, 
                $"Failed to retrieve archive items: {ex.Message}");
        }
    }

    private async Task<Result<IEnumerable<ArchiveItemDto>>> GetArchiveItemsWithDeferredLimitAsync(
        string? entityType,
        RestoreStatus? status,
        int limit,
        Guid actingUserId,
        CancellationToken cancellationToken)
    {
        const int pageSize = 100;

        var readableItems = new List<ArchiveItem>();
        var offset = 0;

        while (readableItems.Count < limit)
        {
            var queryPage = await QueryArchiveItemsPageAsync(
                entityType,
                status,
                pageSize,
                offset,
                cancellationToken);

            var pageItems = queryPage.Items;

            if (queryPage.RetrievedCount == 0)
                break;

            offset += queryPage.RetrievedCount;

            if (pageItems.Count == 0)
            {
                if (queryPage.RetrievedCount < pageSize)
                    break;

                continue;
            }

            var candidateBoardIds = pageItems.Select(item => item.BoardId).Distinct().ToList();
            var readableBoardIdsResult = await _authorizationService!.GetReadableBoardIdsAsync(
                actingUserId,
                candidateBoardIds,
                cancellationToken);

            if (!readableBoardIdsResult.IsSuccess)
            {
                return Result.Failure<IEnumerable<ArchiveItemDto>>(
                    readableBoardIdsResult.ErrorCode,
                    readableBoardIdsResult.ErrorMessage);
            }

            var readableBoardIds = readableBoardIdsResult.Value;
            readableItems.AddRange(pageItems.Where(item => readableBoardIds.Contains(item.BoardId)));

            if (queryPage.RetrievedCount < pageSize)
                break;
        }

        var limitedItems = readableItems
            .OrderByDescending(item => item.ArchivedAt)
            .Take(limit)
            .Select(MapToDto);

        return Result.Success<IEnumerable<ArchiveItemDto>>(limitedItems);
    }

    private async Task<ArchiveItemsQueryPage> QueryArchiveItemsPageAsync(
        string? entityType,
        RestoreStatus? status,
        int pageSize,
        int offset,
        CancellationToken cancellationToken)
    {
        if (entityType is not null)
        {
            var entityTypeItems = (await _unitOfWork.ArchiveItems.GetByEntityTypeAsync(
                entityType,
                pageSize,
                cancellationToken,
                offset)).ToList();

            if (!status.HasValue)
            {
                return new ArchiveItemsQueryPage(entityTypeItems, entityTypeItems.Count);
            }

            var filteredItems = entityTypeItems
                .Where(item => item.RestoreStatus == status.Value)
                .ToList();
            return new ArchiveItemsQueryPage(filteredItems, entityTypeItems.Count);
        }

        if (status.HasValue)
        {
            var statusItems = (await _unitOfWork.ArchiveItems.GetByStatusAsync(
                status.Value,
                pageSize,
                cancellationToken,
                offset)).ToList();
            return new ArchiveItemsQueryPage(statusItems, statusItems.Count);
        }

        var pageItems = (await _unitOfWork.ArchiveItems.GetPageAsync(
            pageSize,
            cancellationToken,
            offset)).ToList();
        return new ArchiveItemsQueryPage(pageItems, pageItems.Count);
    }

    private sealed record ArchiveItemsQueryPage(IReadOnlyList<ArchiveItem> Items, int RetrievedCount);

    public async Task<Result<ArchiveItemDto>> GetArchiveItemByIdAsync(
        Guid id,
        Guid? actingUserId = null,
        CancellationToken cancellationToken = default)
    {
        if (actingUserId.HasValue && actingUserId.Value == Guid.Empty)
            return Result.Failure<ArchiveItemDto>(ErrorCodes.ValidationError, "Acting user ID cannot be empty");

        var archiveItem = await _unitOfWork.ArchiveItems.GetByIdAsync(id, cancellationToken);
        if (archiveItem == null)
            return Result.Failure<ArchiveItemDto>(ErrorCodes.NotFound, $"Archive item with ID {id} not found");

        if (_authorizationService is not null && actingUserId.HasValue)
        {
            var boardReadPermission = await _authorizationService.CanReadBoardAsync(
                actingUserId.Value,
                archiveItem.BoardId);

            if (!boardReadPermission.IsSuccess)
                return Result.Failure<ArchiveItemDto>(boardReadPermission.ErrorCode, boardReadPermission.ErrorMessage);

            if (!boardReadPermission.Value)
                return Result.Failure<ArchiveItemDto>(ErrorCodes.Forbidden, "You do not have access to this archive item");
        }

        return Result.Success(MapToDto(archiveItem));
    }

    public async Task<Result<ArchiveItemDto>> GetArchiveItemByEntityAsync(
        string entityType,
        Guid entityId,
        Guid? actingUserId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(entityType))
            return Result.Failure<ArchiveItemDto>(ErrorCodes.ValidationError, "EntityType cannot be empty");
        if (entityId == Guid.Empty)
            return Result.Failure<ArchiveItemDto>(ErrorCodes.ValidationError, "EntityId cannot be empty");
        if (actingUserId.HasValue && actingUserId.Value == Guid.Empty)
            return Result.Failure<ArchiveItemDto>(ErrorCodes.ValidationError, "Acting user ID cannot be empty");

        var normalizedType = entityType.Trim().ToLowerInvariant();
        if (normalizedType != "board" && normalizedType != "column" && normalizedType != "card")
            return Result.Failure<ArchiveItemDto>(ErrorCodes.ValidationError, "EntityType must be 'board', 'column', or 'card'");

        var archiveItem = await _unitOfWork.ArchiveItems.GetByEntityAsync(normalizedType, entityId, cancellationToken);
        if (archiveItem == null)
            return Result.Failure<ArchiveItemDto>(ErrorCodes.NotFound, $"Archive item for {normalizedType} with entity ID {entityId} not found");

        if (_authorizationService is not null && actingUserId.HasValue)
        {
            var boardReadPermission = await _authorizationService.CanReadBoardAsync(
                actingUserId.Value,
                archiveItem.BoardId);

            if (!boardReadPermission.IsSuccess)
                return Result.Failure<ArchiveItemDto>(boardReadPermission.ErrorCode, boardReadPermission.ErrorMessage);

            if (!boardReadPermission.Value)
                return Result.Failure<ArchiveItemDto>(ErrorCodes.Forbidden, "You do not have access to this archive item");
        }

        return Result.Success(MapToDto(archiveItem));
    }

    public async Task<Result<RestoreResult>> RestoreArchiveItemAsync(
        Guid id,
        RestoreArchiveItemDto dto,
        Guid restoredByUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. Get archive item
            var archiveItem = await _unitOfWork.ArchiveItems.GetByIdAsync(id, cancellationToken);
            if (archiveItem == null)
                return Result.Failure<RestoreResult>(ErrorCodes.NotFound, $"Archive item with ID {id} not found");

            if (archiveItem.RestoreStatus != RestoreStatus.Available)
                return Result.Failure<RestoreResult>(
                    ErrorCodes.InvalidOperation, 
                    $"Cannot restore archive item with status {archiveItem.RestoreStatus}");

            // 2. Determine target board
            var targetBoardId = dto.TargetBoardId ?? archiveItem.BoardId;

            // 3. Check permissions
            if (_authorizationService != null)
            {
                var canWriteResult = await _authorizationService.CanWriteBoardAsync(restoredByUserId, targetBoardId);
                if (!canWriteResult.IsSuccess)
                {
                    return Result.Failure<RestoreResult>(
                        canWriteResult.ErrorCode,
                        canWriteResult.ErrorMessage);
                }

                if (!canWriteResult.Value)
                {
                    return Result.Failure<RestoreResult>(
                        ErrorCodes.Forbidden, 
                        "User does not have permission to restore to target board");
                }
            }

            // 4. Validate and restore based on entity type
            Result<RestoreResult> restoreResult;
            switch (archiveItem.EntityType)
            {
                case "board":
                    restoreResult = await RestoreBoardAsync(archiveItem, dto, restoredByUserId, cancellationToken);
                    break;
                case "column":
                {
                    var targetBoard = await _unitOfWork.Boards.GetByIdAsync(targetBoardId, cancellationToken);
                    if (targetBoard == null)
                        return Result.Failure<RestoreResult>(ErrorCodes.NotFound, $"Target board with ID {targetBoardId} not found");
                    if (targetBoard.IsArchived)
                        return Result.Failure<RestoreResult>(ErrorCodes.InvalidOperation, "Cannot restore to an archived board");

                    restoreResult = await RestoreColumnAsync(archiveItem, targetBoardId, dto, restoredByUserId, cancellationToken);
                    break;
                }
                case "card":
                {
                    var targetBoard = await _unitOfWork.Boards.GetByIdAsync(targetBoardId, cancellationToken);
                    if (targetBoard == null)
                        return Result.Failure<RestoreResult>(ErrorCodes.NotFound, $"Target board with ID {targetBoardId} not found");
                    if (targetBoard.IsArchived)
                        return Result.Failure<RestoreResult>(ErrorCodes.InvalidOperation, "Cannot restore to an archived board");

                    restoreResult = await RestoreCardAsync(archiveItem, targetBoardId, dto, restoredByUserId, cancellationToken);
                    break;
                }
                default:
                    return Result.Failure<RestoreResult>(
                        ErrorCodes.ValidationError, 
                        $"Unknown entity type: {archiveItem.EntityType}");
            }

            if (!restoreResult.IsSuccess)
                return restoreResult;

            // 6. Mark archive item as restored
            archiveItem.MarkAsRestored(restoredByUserId);

            // 7. Create audit log
            var auditLog = new AuditLog(
                "ArchiveItem",
                archiveItem.Id,
                AuditAction.Updated,
                restoredByUserId,
                $"Restored {archiveItem.EntityType} '{restoreResult.Value.ResolvedName ?? archiveItem.Name}' " +
                $"(Original ID: {archiveItem.EntityId}, Restored ID: {restoreResult.Value.RestoredEntityId})");
            await _unitOfWork.AuditLogs.AddAsync(auditLog, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return restoreResult;
        }
        catch (DomainException ex)
        {
            return Result.Failure<RestoreResult>(ex.ErrorCode, ex.Message);
        }
        catch (Exception ex)
        {
            return Result.Failure<RestoreResult>(
                ErrorCodes.UnexpectedError, 
                $"Failed to restore archive item: {ex.Message}");
        }
    }

    private async Task<Result<RestoreResult>> RestoreBoardAsync(
        ArchiveItem archiveItem,
        RestoreArchiveItemDto dto,
        Guid restoredByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Deserialize snapshot
            var snapshot = JsonSerializer.Deserialize<BoardSnapshot>(archiveItem.SnapshotJson);
            if (snapshot == null)
                return Result.Failure<RestoreResult>(
                    ErrorCodes.ValidationError, 
                    "Failed to deserialize board snapshot");

            // Check for naming conflicts
            var existingBoards = await _unitOfWork.Boards.SearchAsync(snapshot.Name, includeArchived: false, cancellationToken);
            var conflictExists = existingBoards.Any(b => b.Name == snapshot.Name);

            var nameResult = ArchiveConflictDetector.ResolveName(
                snapshot.Name, conflictExists, dto.ConflictStrategy, "board");
            if (!nameResult.IsSuccess)
                return Result.Failure<RestoreResult>(nameResult.ErrorCode, nameResult.ErrorMessage);
            var resolvedName = nameResult.Value;

            // For InPlace mode, unarchive existing board if it's archived
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

            // Create new board (Copy mode or InPlace when original doesn't exist)
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

    private async Task<Result<RestoreResult>> RestoreColumnAsync(
        ArchiveItem archiveItem,
        Guid targetBoardId,
        RestoreArchiveItemDto dto,
        Guid restoredByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Deserialize snapshot
            var snapshot = JsonSerializer.Deserialize<ColumnSnapshot>(archiveItem.SnapshotJson);
            if (snapshot == null)
                return Result.Failure<RestoreResult>(
                    ErrorCodes.ValidationError, 
                    "Failed to deserialize column snapshot");

            // Get board and existing columns
            var board = await _unitOfWork.Boards.GetByIdWithDetailsAsync(targetBoardId, cancellationToken);
            if (board == null)
                return Result.Failure<RestoreResult>(ErrorCodes.NotFound, $"Board with ID {targetBoardId} not found");

            // Check for naming conflicts
            var conflictExists = board.Columns.Any(c => c.Name == snapshot.Name);

            var nameResult = ArchiveConflictDetector.ResolveName(
                snapshot.Name, conflictExists, dto.ConflictStrategy, "column");
            if (!nameResult.IsSuccess)
                return Result.Failure<RestoreResult>(nameResult.ErrorCode, nameResult.ErrorMessage);
            var resolvedName = nameResult.Value;

            // Determine position (add to end)
            var maxPosition = board.Columns.Any() ? board.Columns.Max(c => c.Position) : -1;
            var newPosition = maxPosition + 1;

            // Create new column
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

    private async Task<Result<RestoreResult>> RestoreCardAsync(
        ArchiveItem archiveItem,
        Guid targetBoardId,
        RestoreArchiveItemDto dto,
        Guid restoredByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Deserialize snapshot
            var snapshot = JsonSerializer.Deserialize<CardSnapshot>(archiveItem.SnapshotJson);
            if (snapshot == null)
                return Result.Failure<RestoreResult>(
                    ErrorCodes.ValidationError, 
                    "Failed to deserialize card snapshot");

            // Get board with details
            var board = await _unitOfWork.Boards.GetByIdWithDetailsAsync(targetBoardId, cancellationToken);
            if (board == null)
                return Result.Failure<RestoreResult>(ErrorCodes.NotFound, $"Board with ID {targetBoardId} not found");

            // Find target column
            Column? targetColumn = null;
            if (snapshot.ColumnId != Guid.Empty)
            {
                targetColumn = board.Columns.FirstOrDefault(c => c.Id == snapshot.ColumnId);
            }

            // If original column doesn't exist, use first available column
            if (targetColumn == null)
            {
                targetColumn = board.Columns.OrderBy(c => c.Position).FirstOrDefault();
                if (targetColumn == null)
                    return Result.Failure<RestoreResult>(
                        ErrorCodes.InvalidOperation, 
                        "Target board has no columns to restore card to");
            }

            // Get column with cards to check WIP limit and position
            var columnWithCards = await _unitOfWork.Columns.GetByIdWithCardsAsync(targetColumn.Id, cancellationToken);
            if (columnWithCards == null)
                return Result.Failure<RestoreResult>(ErrorCodes.NotFound, $"Column with ID {targetColumn.Id} not found");

            // Check WIP limit
            if (columnWithCards.WouldExceedWipLimitIfAdded())
                return Result.Failure<RestoreResult>(
                    ErrorCodes.WipLimitExceeded, 
                    $"Cannot restore card, column '{columnWithCards.Name}' has reached its WIP limit");

            // Check for title conflicts
            var existingCards = columnWithCards.Cards.ToList();
            var conflictExists = existingCards.Any(c => c.Title == snapshot.Title);

            var nameResult = ArchiveConflictDetector.ResolveName(
                snapshot.Title, conflictExists, dto.ConflictStrategy, "card");
            if (!nameResult.IsSuccess)
                return Result.Failure<RestoreResult>(nameResult.ErrorCode, nameResult.ErrorMessage);
            var resolvedTitle = nameResult.Value;

            // Determine position (add to bottom)
            var maxPosition = existingCards.Any() ? existingCards.Max(c => c.Position) : -1;
            var newPosition = maxPosition + 1;

            // Create new card
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

    private static ArchiveItemDto MapToDto(ArchiveItem item)
    {
        return new ArchiveItemDto(
            item.Id,
            item.EntityType,
            item.EntityId,
            item.BoardId,
            item.Name,
            item.ArchivedByUserId,
            item.ArchivedAt,
            item.Reason,
            item.RestoreStatus,
            item.RestoredAt,
            item.RestoredByUserId,
            item.CreatedAt,
            item.UpdatedAt);
    }
}

// Snapshot DTOs for deserialization
internal record BoardSnapshot(string Name, string? Description);
internal record ColumnSnapshot(string Name, int Position, int? WipLimit);
internal record CardSnapshot(
    string Title, 
    string? Description, 
    DateTimeOffset? DueDate, 
    bool IsBlocked, 
    string? BlockReason,
    Guid ColumnId);
