using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class BoardService
{
    private readonly IUnitOfWork _unitOfWork;

    public BoardService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<BoardDto>> CreateBoardAsync(CreateBoardDto dto, CancellationToken cancellationToken = default)
    {
        try
        {
            var board = new Board(dto.Name, dto.Description);
            await _unitOfWork.Boards.AddAsync(board, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(MapToDto(board));
        }
        catch (DomainException ex)
        {
            return Result.Failure<BoardDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<BoardDto>> UpdateBoardAsync(Guid id, UpdateBoardDto dto, CancellationToken cancellationToken = default)
    {
        try
        {
            var board = await _unitOfWork.Boards.GetByIdAsync(id, cancellationToken);
            if (board == null)
                return Result.Failure<BoardDto>(ErrorCodes.NotFound, $"Board with ID {id} not found");

            if (dto.Name != null || dto.Description != null)
                board.Update(dto.Name, dto.Description);

            if (dto.IsArchived.HasValue)
            {
                if (dto.IsArchived.Value)
                    board.Archive();
                else
                    board.Unarchive();
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success(MapToDto(board));
        }
        catch (DomainException ex)
        {
            return Result.Failure<BoardDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<BoardDto>> GetBoardByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var board = await _unitOfWork.Boards.GetByIdAsync(id, cancellationToken);
        if (board == null)
            return Result.Failure<BoardDto>(ErrorCodes.NotFound, $"Board with ID {id} not found");

        return Result.Success(MapToDto(board));
    }

    public async Task<Result<BoardDetailDto>> GetBoardDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var board = await _unitOfWork.Boards.GetByIdWithDetailsAsync(id, cancellationToken);
        if (board == null)
            return Result.Failure<BoardDetailDto>(ErrorCodes.NotFound, $"Board with ID {id} not found");

        return Result.Success(MapToDetailDto(board));
    }

    public async Task<Result<IEnumerable<BoardDto>>> ListBoardsAsync(string? searchText = null, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var boards = await _unitOfWork.Boards.SearchAsync(searchText, includeArchived, cancellationToken);
        return Result.Success(boards.Select(MapToDto));
    }

    public async Task<Result> DeleteBoardAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var board = await _unitOfWork.Boards.GetByIdAsync(id, cancellationToken);
        if (board == null)
            return Result.Failure(ErrorCodes.NotFound, $"Board with ID {id} not found");

        board.Archive(); // Soft delete
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static BoardDto MapToDto(Board board)
    {
        return new BoardDto(
            board.Id,
            board.Name,
            board.Description,
            board.IsArchived,
            board.CreatedAt,
            board.UpdatedAt
        );
    }

    private BoardDetailDto MapToDetailDto(Board board)
    {
        var columns = board.Columns
            .OrderBy(c => c.Position)
            .Select(c => new ColumnDto(
                c.Id,
                c.BoardId,
                c.Name,
                c.Position,
                c.WipLimit,
                c.Cards.Count,
                c.CreatedAt,
                c.UpdatedAt
            ))
            .ToList();

        return new BoardDetailDto(
            board.Id,
            board.Name,
            board.Description,
            board.IsArchived,
            board.CreatedAt,
            board.UpdatedAt,
            columns
        );
    }
}
