using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class LabelService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBoardRealtimeNotifier _realtimeNotifier;
    private readonly IHistoryService? _historyService;

    public LabelService(IUnitOfWork unitOfWork)
        : this(unitOfWork, realtimeNotifier: null, historyService: null)
    {
    }

    public LabelService(IUnitOfWork unitOfWork, IBoardRealtimeNotifier? realtimeNotifier = null, IHistoryService? historyService = null)
    {
        _unitOfWork = unitOfWork;
        _realtimeNotifier = realtimeNotifier ?? NoOpBoardRealtimeNotifier.Instance;
        _historyService = historyService;
    }

    private async Task SafeLogAsync(string entityType, Guid entityId, AuditAction action, Guid? userId = null, string? changes = null)
    {
        if (_historyService == null) return;
        try { await _historyService.LogActionAsync(entityType, entityId, action, userId, changes); }
        catch (Exception) { /* Audit is secondary — never crash the mutation */ }
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
            await _realtimeNotifier.NotifyBoardMutationAsync(
                new BoardRealtimeEvent(label.BoardId, "label", "created", label.Id, DateTimeOffset.UtcNow),
                cancellationToken);
            await SafeLogAsync("label", label.Id, AuditAction.Created, changes: $"name={label.Name}");

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

            // Capture pre-mutation state for change summary
            var oldName = label.Name;
            var oldColorHex = label.ColorHex;

            label.Update(dto.Name, dto.ColorHex);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _realtimeNotifier.NotifyBoardMutationAsync(
                new BoardRealtimeEvent(label.BoardId, "label", "updated", label.Id, DateTimeOffset.UtcNow),
                cancellationToken);

            var changeSummary = BuildLabelChangeSummary(dto, oldName, oldColorHex);
            await SafeLogAsync("label", label.Id, AuditAction.Updated, changes: changeSummary);

            return Result.Success(MapToDto(label));
        }
        catch (DomainException ex)
        {
            return Result.Failure<LabelDto>(ex.ErrorCode, ex.Message);
        }
    }

    private static string BuildLabelChangeSummary(UpdateLabelDto dto, string oldName, string oldColorHex)
    {
        var parts = new List<string>();
        if (dto.Name != null)
            parts.Add($"Name: '{oldName}' -> '{dto.Name}'");
        if (dto.ColorHex != null)
            parts.Add($"Color: '{oldColorHex}' -> '{dto.ColorHex}'");
        return parts.Count > 0 ? string.Join("; ", parts) : "no fields changed";
    }

    public async Task<Result<LabelDto>> UpdateLabelAsync(Guid boardId, Guid id, UpdateLabelDto dto, CancellationToken cancellationToken = default)
    {
        var label = await _unitOfWork.Labels.GetByIdAsync(id, cancellationToken);
        if (label == null || label.BoardId != boardId)
            return Result.Failure<LabelDto>(ErrorCodes.NotFound, $"Label with ID {id} not found in board {boardId}");

        return await UpdateLabelAsync(id, dto, cancellationToken);
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
        await _realtimeNotifier.NotifyBoardMutationAsync(
            new BoardRealtimeEvent(label.BoardId, "label", "deleted", label.Id, DateTimeOffset.UtcNow),
            cancellationToken);
        await SafeLogAsync("label", label.Id, AuditAction.Deleted, changes: $"name={label.Name}");

        return Result.Success();
    }

    public async Task<Result> DeleteLabelAsync(Guid boardId, Guid id, CancellationToken cancellationToken = default)
    {
        var label = await _unitOfWork.Labels.GetByIdAsync(id, cancellationToken);
        if (label == null || label.BoardId != boardId)
            return Result.Failure(ErrorCodes.NotFound, $"Label with ID {id} not found in board {boardId}");

        return await DeleteLabelAsync(id, cancellationToken);
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
