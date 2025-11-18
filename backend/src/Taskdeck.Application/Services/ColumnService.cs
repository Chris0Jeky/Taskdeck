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
