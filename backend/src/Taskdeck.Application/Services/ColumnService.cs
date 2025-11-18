using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class ColumnService
{
    private readonly IUnitOfWork _unitOfWork;

    public ColumnService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

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

            column.Update(dto.Name, dto.WipLimit, dto.Position);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(MapToDto(column));
        }
        catch (DomainException ex)
        {
            return Result.Failure<ColumnDto>(ex.ErrorCode, ex.Message);
        }
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

        return Result.Success();
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

            // Two-phase update to avoid UNIQUE constraint violations:
            // Phase 1: Set all positions to temporary negative values
            for (int i = 0; i < dto.ColumnIds.Count; i++)
            {
                var column = columnDict[dto.ColumnIds[i]];
                column.Update(null, null, -(i + 1));
            }

            // Save Phase 1 changes - moves all columns to negative positions
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Phase 2: Set correct positive positions
            for (int i = 0; i < dto.ColumnIds.Count; i++)
            {
                var column = columnDict[dto.ColumnIds[i]];
                column.Update(null, null, i);
            }

            // Save Phase 2 changes - moves columns to final positions
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Return reordered columns
            var reorderedColumns = dto.ColumnIds.Select(id => MapToDto(columnDict[id]));
            return Result.Success(reorderedColumns);
        }
        catch (DomainException ex)
        {
            return Result.Failure<IEnumerable<ColumnDto>>(ex.ErrorCode, ex.Message);
        }
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
