using Microsoft.Extensions.Logging;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class ColumnService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBoardRealtimeNotifier _realtimeNotifier;
    private readonly IHistoryService? _historyService;
    private readonly ILogger<ColumnService>? _logger;

    public ColumnService(IUnitOfWork unitOfWork)
        : this(unitOfWork, realtimeNotifier: null, historyService: null)
    {
    }

    public ColumnService(IUnitOfWork unitOfWork, IBoardRealtimeNotifier? realtimeNotifier = null, IHistoryService? historyService = null, ILogger<ColumnService>? logger = null)
    {
        _unitOfWork = unitOfWork;
        _realtimeNotifier = realtimeNotifier ?? NoOpBoardRealtimeNotifier.Instance;
        _historyService = historyService;
        _logger = logger;
    }

    private Task SafeLogAsync(string entityType, Guid entityId, AuditAction action, Guid? userId = null, string? changes = null)
        => AuditLogWriter.SafeLogAsync(_historyService, _logger, entityType, entityId, action, userId, changes);

    public async Task<Result<ColumnDto>> CreateColumnAsync(CreateColumnDto dto, CancellationToken cancellationToken = default)
    {
        try
        {
            // Verify board exists
            var board = await _unitOfWork.Boards.GetByIdAsync(dto.BoardId, cancellationToken);
            if (board == null)
                return Result.Failure<ColumnDto>(ErrorCodes.NotFound, $"Board with ID {dto.BoardId} not found");

            // Determine position if not provided
            var position = dto.Position;
            if (!position.HasValue)
            {
                var existingColumns = await _unitOfWork.Columns.GetByBoardIdAsync(dto.BoardId, cancellationToken);
                position = existingColumns.Any() ? existingColumns.Max(c => c.Position) + 1 : 0;
            }

            var column = new Column(dto.BoardId, dto.Name, position.Value, dto.WipLimit);
            await _unitOfWork.Columns.AddAsync(column, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _realtimeNotifier.NotifyBoardMutationAsync(
                new BoardRealtimeEvent(column.BoardId, "column", "created", column.Id, DateTimeOffset.UtcNow),
                cancellationToken);
            await SafeLogAsync("column", column.Id, AuditAction.Created, changes: $"name={column.Name}");

            return Result.Success(MapToDto(column));
        }
        catch (DomainException ex)
        {
            return Result.Failure<ColumnDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<ColumnDto>> UpdateColumnAsync(Guid id, UpdateColumnDto dto, CancellationToken cancellationToken = default)
    {
        try
        {
            var column = await _unitOfWork.Columns.GetByIdAsync(id, cancellationToken);
            if (column == null)
                return Result.Failure<ColumnDto>(ErrorCodes.NotFound, $"Column with ID {id} not found");

            // Capture pre-mutation state for change summary
            var oldName = column.Name;
            var oldWipLimit = column.WipLimit;
            var oldPosition = column.Position;

            column.Update(dto.Name, dto.WipLimit, dto.Position);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _realtimeNotifier.NotifyBoardMutationAsync(
                new BoardRealtimeEvent(column.BoardId, "column", "updated", column.Id, DateTimeOffset.UtcNow),
                cancellationToken);

            var changeSummary = BuildColumnChangeSummary(dto, oldName, oldWipLimit, oldPosition);
            await SafeLogAsync("column", column.Id, AuditAction.Updated, changes: changeSummary);

            return Result.Success(MapToDto(column));
        }
        catch (DomainException ex)
        {
            return Result.Failure<ColumnDto>(ex.ErrorCode, ex.Message);
        }
    }

    private static string BuildColumnChangeSummary(UpdateColumnDto dto, string oldName, int? oldWipLimit, int oldPosition)
    {
        var parts = new List<string>();
        if (dto.Name != null && dto.Name != oldName)
            parts.Add($"Name: '{oldName}' -> '{dto.Name}'");
        if (dto.WipLimit.HasValue && dto.WipLimit.Value != oldWipLimit)
            parts.Add($"WipLimit: {oldWipLimit?.ToString() ?? "none"} -> {dto.WipLimit.Value}");
        if (dto.Position.HasValue && dto.Position.Value != oldPosition)
            parts.Add($"Position: {oldPosition} -> {dto.Position.Value}");
        return parts.Count > 0 ? string.Join("; ", parts) : "no fields changed";
    }

    public async Task<Result<ColumnDto>> UpdateColumnAsync(Guid boardId, Guid id, UpdateColumnDto dto, CancellationToken cancellationToken = default)
    {
        var column = await _unitOfWork.Columns.GetByIdAsync(id, cancellationToken);
        if (column == null || column.BoardId != boardId)
            return Result.Failure<ColumnDto>(ErrorCodes.NotFound, $"Column with ID {id} not found in board {boardId}");

        return await UpdateColumnAsync(id, dto, cancellationToken);
    }

    public async Task<Result<IEnumerable<ColumnDto>>> GetColumnsByBoardIdAsync(Guid boardId, CancellationToken cancellationToken = default)
    {
        var columns = await _unitOfWork.Columns.GetByBoardIdAsync(boardId, cancellationToken);
        return Result.Success(columns.OrderBy(c => c.Position).Select(MapToDto));
    }

    public async Task<Result> DeleteColumnAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var column = await _unitOfWork.Columns.GetByIdWithCardsAsync(id, cancellationToken);
        if (column == null)
            return Result.Failure(ErrorCodes.NotFound, $"Column with ID {id} not found");

        if (column.Cards.Any())
            return Result.Failure(ErrorCodes.Conflict, "Cannot delete column that contains cards");

        await _unitOfWork.Columns.DeleteAsync(column, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _realtimeNotifier.NotifyBoardMutationAsync(
            new BoardRealtimeEvent(column.BoardId, "column", "deleted", column.Id, DateTimeOffset.UtcNow),
            cancellationToken);
        await SafeLogAsync("column", column.Id, AuditAction.Deleted, changes: $"name={column.Name}");

        return Result.Success();
    }

    public async Task<Result> DeleteColumnAsync(Guid boardId, Guid id, CancellationToken cancellationToken = default)
    {
        var column = await _unitOfWork.Columns.GetByIdWithCardsAsync(id, cancellationToken);
        if (column == null || column.BoardId != boardId)
            return Result.Failure(ErrorCodes.NotFound, $"Column with ID {id} not found in board {boardId}");

        return await DeleteColumnAsync(id, cancellationToken);
    }

    public async Task<Result<IEnumerable<ColumnDto>>> ReorderColumnsAsync(Guid boardId, ReorderColumnsDto dto, CancellationToken cancellationToken = default)
    {
        try
        {
            // Verify board exists
            var board = await _unitOfWork.Boards.GetByIdAsync(boardId, cancellationToken);
            if (board == null)
                return Result.Failure<IEnumerable<ColumnDto>>(ErrorCodes.NotFound, $"Board with ID {boardId} not found");

            // Get all columns for the board
            var allColumns = await _unitOfWork.Columns.GetByBoardIdAsync(boardId, cancellationToken);
            var columnsList = allColumns.ToList();

            // Validate that all column IDs in the request exist and belong to this board
            var columnDict = columnsList.ToDictionary(c => c.Id);
            foreach (var columnId in dto.ColumnIds)
            {
                if (!columnDict.ContainsKey(columnId))
                    return Result.Failure<IEnumerable<ColumnDto>>(ErrorCodes.NotFound, $"Column with ID {columnId} not found in board {boardId}");
            }

            // Validate that all columns in the board are included in the request
            if (dto.ColumnIds.Count != columnsList.Count)
                return Result.Failure<IEnumerable<ColumnDto>>(ErrorCodes.ValidationError, "Reorder request must include all columns in the board");

            // Reindex positions in the requested order. This preserves each column's
            // WipLimit and Name — only the position changes, so a reorder is lossless.
            var orderedColumns = dto.ColumnIds.Select(id => columnDict[id]).ToList();
            await ApplyColumnOrderAsync(orderedColumns, cancellationToken);

            await _realtimeNotifier.NotifyBoardMutationAsync(
                new BoardRealtimeEvent(boardId, "column", "reordered", null, DateTimeOffset.UtcNow),
                cancellationToken);
            await SafeLogAsync("column", boardId, AuditAction.Updated, changes: $"reordered; count={dto.ColumnIds.Count}");

            // Return reordered columns
            var reorderedColumns = dto.ColumnIds.Select(id => MapToDto(columnDict[id]));
            return Result.Success(reorderedColumns);
        }
        catch (DomainException ex)
        {
            return Result.Failure<IEnumerable<ColumnDto>>(ex.ErrorCode, ex.Message);
        }
    }

    /// <summary>
    /// Moves a single column to <paramref name="newPosition"/> within its board and
    /// reindexes the remaining columns to a contiguous 0..n-1 sequence. The move is
    /// atomic (no transient unique-index collision) and lossless (WipLimit/Name are
    /// preserved). Used by the proposal "reorder column" apply operation.
    /// </summary>
    public async Task<Result<ColumnDto>> ReorderColumnAsync(Guid columnId, int newPosition, CancellationToken cancellationToken = default)
    {
        try
        {
            if (newPosition < 0)
                return Result.Failure<ColumnDto>(ErrorCodes.ValidationError, "Position cannot be negative");

            var column = await _unitOfWork.Columns.GetByIdAsync(columnId, cancellationToken);
            if (column == null)
                return Result.Failure<ColumnDto>(ErrorCodes.NotFound, $"Column with ID {columnId} not found");

            var boardColumns = (await _unitOfWork.Columns.GetByBoardIdAsync(column.BoardId, cancellationToken))
                .OrderBy(c => c.Position)
                .ToList();
            var oldPosition = column.Position;

            // Rebuild the desired order: drop the moved column, then insert it at the
            // requested index (clamped to the end when the request overshoots).
            var ordered = boardColumns.Where(c => c.Id != column.Id).ToList();
            var insertAt = Math.Min(newPosition, ordered.Count);
            ordered.Insert(insertAt, column);

            await ApplyColumnOrderAsync(ordered, cancellationToken);

            await _realtimeNotifier.NotifyBoardMutationAsync(
                new BoardRealtimeEvent(column.BoardId, "column", "reordered", column.Id, DateTimeOffset.UtcNow),
                cancellationToken);
            await SafeLogAsync("column", column.Id, AuditAction.Updated, changes: $"Position: {oldPosition} -> {column.Position}");

            return Result.Success(MapToDto(column));
        }
        catch (DomainException ex)
        {
            return Result.Failure<ColumnDto>(ex.ErrorCode, ex.Message);
        }
    }

    /// <summary>
    /// Reassigns positions 0..n-1 to <paramref name="orderedColumns"/> in list order.
    /// Phase 1 parks every column above the current maximum position, then phase 2 writes
    /// the final contiguous positions. The two-phase write means no intermediate state
    /// violates the unique (BoardId, Position) index (SQLite checks the constraint per
    /// row, not deferred to commit). Only positions change — WipLimit and Name are kept.
    /// </summary>
    private async Task ApplyColumnOrderAsync(IReadOnlyList<Column> orderedColumns, CancellationToken cancellationToken)
    {
        if (orderedColumns.Count == 0)
            return;

        var parkBase = orderedColumns.Max(c => c.Position) + 1;
        for (var i = 0; i < orderedColumns.Count; i++)
            orderedColumns[i].SetPosition(parkBase + i);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        for (var i = 0; i < orderedColumns.Count; i++)
            orderedColumns[i].SetPosition(i);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static ColumnDto MapToDto(Column column)
    {
        return new ColumnDto(
            column.Id,
            column.BoardId,
            column.Name,
            column.Position,
            column.WipLimit,
            column.Cards.Count,
            column.CreatedAt,
            column.UpdatedAt
        );
    }
}
