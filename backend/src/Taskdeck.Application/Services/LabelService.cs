using Microsoft.Extensions.Logging;
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
    private readonly ILogger<LabelService>? _logger;

    public LabelService(IUnitOfWork unitOfWork)
        : this(unitOfWork, realtimeNotifier: null, historyService: null)
    {
    }

    public LabelService(IUnitOfWork unitOfWork, IBoardRealtimeNotifier? realtimeNotifier = null, IHistoryService? historyService = null, ILogger<LabelService>? logger = null)
    {
        _unitOfWork = unitOfWork;
        _realtimeNotifier = realtimeNotifier ?? NoOpBoardRealtimeNotifier.Instance;
        _historyService = historyService;
        _logger = logger;
    }

    private Task SafeLogAsync(string entityType, Guid entityId, AuditAction action, Guid? userId = null, string? changes = null)
        => AuditLogWriter.SafeLogAsync(_historyService, _logger, entityType, entityId, action, userId, changes);

    /// <summary>
    /// Positional-token overload kept so existing <c>(dto, cancellationToken)</c> call sites keep
    /// binding to the no-actor path instead of failing to compile against the actor parameter.
    /// No proposal lane reaches <c>LabelService</c>: <c>OperationHandlerRegistry</c> only resolves
    /// existing labels and routes its writes through <c>CardService</c>, and the MCP surface reads
    /// labels only. (Starter-pack apply and board import create labels via the repository directly,
    /// bypassing this service and its audit rows entirely.) <c>LabelsController</c> is therefore the
    /// sole request-path caller and always supplies an actor; the unattributed path exists for
    /// non-request callers (today, tests).
    /// </summary>
    public Task<Result<LabelDto>> CreateLabelAsync(CreateLabelDto dto, CancellationToken cancellationToken)
    {
        return CreateLabelAsync(dto, actorUserId: null, cancellationToken);
    }

    public async Task<Result<LabelDto>> CreateLabelAsync(CreateLabelDto dto, Guid? actorUserId = null, CancellationToken cancellationToken = default)
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
            await SafeLogAsync("label", label.Id, AuditAction.Created, actorUserId, $"name={label.Name}");

            return Result.Success(MapToDto(label));
        }
        catch (DomainException ex)
        {
            return Result.Failure<LabelDto>(ex.ErrorCode, ex.Message);
        }
    }

    public Task<Result<LabelDto>> UpdateLabelAsync(Guid id, UpdateLabelDto dto, CancellationToken cancellationToken)
    {
        return UpdateLabelAsync(id, dto, actorUserId: null, cancellationToken);
    }

    public async Task<Result<LabelDto>> UpdateLabelAsync(Guid id, UpdateLabelDto dto, Guid? actorUserId = null, CancellationToken cancellationToken = default)
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
            await SafeLogAsync("label", label.Id, AuditAction.Updated, actorUserId, changeSummary);

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
        if (dto.Name != null && dto.Name != oldName)
            parts.Add($"Name: '{oldName}' -> '{dto.Name}'");
        if (dto.ColorHex != null && dto.ColorHex != oldColorHex)
            parts.Add($"Color: '{oldColorHex}' -> '{dto.ColorHex}'");
        return parts.Count > 0 ? string.Join("; ", parts) : "no fields changed";
    }

    public Task<Result<LabelDto>> UpdateLabelAsync(Guid boardId, Guid id, UpdateLabelDto dto, CancellationToken cancellationToken)
    {
        return UpdateLabelAsync(boardId, id, dto, actorUserId: null, cancellationToken);
    }

    public async Task<Result<LabelDto>> UpdateLabelAsync(Guid boardId, Guid id, UpdateLabelDto dto, Guid? actorUserId = null, CancellationToken cancellationToken = default)
    {
        var label = await _unitOfWork.Labels.GetByIdAsync(id, cancellationToken);
        if (label == null || label.BoardId != boardId)
            return Result.Failure<LabelDto>(ErrorCodes.NotFound, $"Label with ID {id} not found in board {boardId}");

        return await UpdateLabelAsync(id, dto, actorUserId, cancellationToken);
    }

    public async Task<Result<IEnumerable<LabelDto>>> GetLabelsByBoardIdAsync(Guid boardId, CancellationToken cancellationToken = default)
    {
        var labels = await _unitOfWork.Labels.GetByBoardIdAsync(boardId, cancellationToken);
        return Result.Success(labels.Select(MapToDto));
    }

    public Task<Result> DeleteLabelAsync(Guid id, CancellationToken cancellationToken)
    {
        return DeleteLabelAsync(id, actorUserId: null, cancellationToken);
    }

    public async Task<Result> DeleteLabelAsync(Guid id, Guid? actorUserId = null, CancellationToken cancellationToken = default)
    {
        var label = await _unitOfWork.Labels.GetByIdAsync(id, cancellationToken);
        if (label == null)
            return Result.Failure(ErrorCodes.NotFound, $"Label with ID {id} not found");

        await _unitOfWork.Labels.DeleteAsync(label, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _realtimeNotifier.NotifyBoardMutationAsync(
            new BoardRealtimeEvent(label.BoardId, "label", "deleted", label.Id, DateTimeOffset.UtcNow),
            cancellationToken);
        await SafeLogAsync("label", label.Id, AuditAction.Deleted, actorUserId, $"name={label.Name}");

        return Result.Success();
    }

    public Task<Result> DeleteLabelAsync(Guid boardId, Guid id, CancellationToken cancellationToken)
    {
        return DeleteLabelAsync(boardId, id, actorUserId: null, cancellationToken);
    }

    public async Task<Result> DeleteLabelAsync(Guid boardId, Guid id, Guid? actorUserId = null, CancellationToken cancellationToken = default)
    {
        var label = await _unitOfWork.Labels.GetByIdAsync(id, cancellationToken);
        if (label == null || label.BoardId != boardId)
            return Result.Failure(ErrorCodes.NotFound, $"Label with ID {id} not found in board {boardId}");

        return await DeleteLabelAsync(id, actorUserId, cancellationToken);
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
