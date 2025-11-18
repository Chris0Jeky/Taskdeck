using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class LabelService
{
    private readonly IUnitOfWork _unitOfWork;

    public LabelService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<LabelDto>> CreateLabelAsync(CreateLabelDto dto, CancellationToken cancellationToken = default)
    {
        try
        {
            var board = await _unitOfWork.Boards.GetByIdAsync(dto.BoardId, cancellationToken);
            if (board == null)
                return Result.Failure<LabelDto>(ErrorCodes.NotFound, $"Board with ID {dto.BoardId} not found");

            var label = new Label(dto.BoardId, dto.Name, dto.ColorHex);
            await _unitOfWork.Labels.AddAsync(label, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(MapToDto(label));
        }
        catch (DomainException ex)
        {
            return Result.Failure<LabelDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<LabelDto>> UpdateLabelAsync(Guid id, UpdateLabelDto dto, CancellationToken cancellationToken = default)
    {
        try
        {
            var label = await _unitOfWork.Labels.GetByIdAsync(id, cancellationToken);
            if (label == null)
                return Result.Failure<LabelDto>(ErrorCodes.NotFound, $"Label with ID {id} not found");

            label.Update(dto.Name, dto.ColorHex);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(MapToDto(label));
        }
        catch (DomainException ex)
        {
            return Result.Failure<LabelDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<IEnumerable<LabelDto>>> GetLabelsByBoardIdAsync(Guid boardId, CancellationToken cancellationToken = default)
    {
        var labels = await _unitOfWork.Labels.GetByBoardIdAsync(boardId, cancellationToken);
        return Result.Success(labels.Select(MapToDto));
    }

    public async Task<Result> DeleteLabelAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var label = await _unitOfWork.Labels.GetByIdAsync(id, cancellationToken);
        if (label == null)
            return Result.Failure(ErrorCodes.NotFound, $"Label with ID {id} not found");

        await _unitOfWork.Labels.DeleteAsync(label, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private static LabelDto MapToDto(Label label)
    {
        return new LabelDto(
            label.Id,
            label.BoardId,
            label.Name,
            label.ColorHex,
            label.CreatedAt,
            label.UpdatedAt
        );
    }
}
