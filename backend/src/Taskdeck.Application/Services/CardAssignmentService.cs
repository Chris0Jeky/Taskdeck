using System.Text.Json;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public sealed class CardAssignmentService(
    IUnitOfWork unitOfWork, ICardAssignmentStore store, IAuthorizationService authorization,
    IBoardRealtimeNotifier? notifier = null)
{
    private readonly IBoardRealtimeNotifier _notifier = notifier ?? NoOpBoardRealtimeNotifier.Instance;

    public async Task<Result<IReadOnlyList<BoardParticipantDto>>> ParticipantsAsync(Guid boardId, Guid actorId, CancellationToken ct = default)
    {
        var permission = await authorization.CanReadBoardAsync(actorId, boardId);
        if (!permission.IsSuccess || !permission.Value)
            return Result.Failure<IReadOnlyList<BoardParticipantDto>>(ErrorCodes.Forbidden, "You do not have access to this board.");
        return Result.Success<IReadOnlyList<BoardParticipantDto>>((await store.ReadParticipantsAsync(boardId, ct))
            .Select(u => new BoardParticipantDto(u.Id, u.Username)).ToArray());
    }

    public async Task<Result<CardDto>> ReplaceAsync(Guid boardId, Guid cardId, ReplaceCardAssignmentsDto dto,
        Guid actorId, CancellationToken ct = default)
    {
        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var result = await StageReplaceAsync(boardId, cardId, dto, actorId, ct);
            if (!result.IsSuccess) { await unitOfWork.RollbackTransactionAsync(ct); return result; }
            await unitOfWork.SaveChangesAsync(ct);
            await unitOfWork.CommitTransactionAsync(ct);
            await NotifyAsync(boardId, cardId, ct);
            return result;
        }
        catch (DomainException ex)
        {
            await unitOfWork.RollbackTransactionAsync(ct);
            return Result.Failure<CardDto>(ex.ErrorCode, ex.Message);
        }
        catch { await unitOfWork.RollbackTransactionAsync(ct); throw; }
    }

    // The proposal executor owns the transaction and its post-commit notification.
    public async Task<Result<CardDto>> StageReplaceAsync(Guid boardId, Guid cardId, ReplaceCardAssignmentsDto dto,
        Guid actorId, CancellationToken ct = default)
    {
        if (dto.UserIds is null || dto.ExpectedUpdatedAt is null)
            return Result.Failure<CardDto>(ErrorCodes.ValidationError, "userIds and expectedUpdatedAt are required; use [] to clear assignments.");
        await store.RefreshAuthorityAsync(boardId, actorId, ct);
        var actor = await unitOfWork.Users.GetByIdAsync(actorId, ct);
        var permission = await authorization.CanWriteBoardAsync(actorId, boardId);
        if (actor is not { IsActive: true } || !permission.IsSuccess || !permission.Value)
            return Result.Failure<CardDto>(ErrorCodes.Forbidden, "You do not have permission to assign this card.");
        var board = await unitOfWork.Boards.GetByIdAsync(boardId, ct);
        var card = await store.ReadCardAsync(boardId, cardId, ct);
        if (card is null || board is null) return Result.Failure<CardDto>(ErrorCodes.NotFound, "Card not found in this board.");
        if (card.IsArchived || board.IsArchived)
            return Result.Failure<CardDto>(ErrorCodes.InvalidOperation, "Restore the board and card before changing assignments.");
        if (card.UpdatedAt != dto.ExpectedUpdatedAt)
            return Result.Failure<CardDto>(ErrorCodes.Conflict, "Card changed. Refresh its assignments before retrying.");
        var participants = await store.ReadParticipantsAsync(boardId, ct);
        var eligible = participants.Select(u => u.Id).ToHashSet();
        if (dto.UserIds.Any(id => !eligible.Contains(id)))
            return Result.Failure<CardDto>(ErrorCodes.ValidationError, "Every assignee must be an active participant of this board.");
        var before = card.Assignments.Select(a => a.UserId).Order().ToArray();
        if (card.ReplaceAssignments(dto.UserIds, actorId))
        {
            board.RecordHierarchyMutation();
            await unitOfWork.AuditLogs.AddAsync(new AuditLog("card", card.Id, AuditAction.Updated, actorId,
                JsonSerializer.Serialize(new { reason = "assignment-replace", before, after = dto.UserIds.Distinct().Order().ToArray() })), ct);
        }
        var names = participants.ToDictionary(u => u.Id, u => u.Username);
        return Result.Success(CardService.MapToDto(card) with { Assignments = card.Assignments.OrderBy(a => a.UserId)
            .Select(a => new CardAssignmentDto(a.UserId, names[a.UserId], a.AssignedAt, a.AssignedByUserId)).ToArray() });
    }

    public async Task<IReadOnlyList<Card>> StageDetachAsync(Guid userId, Guid? boardId, Guid actorId, string reason, CancellationToken ct = default)
    {
        var cards = await store.ReadAssignedCardsAsync(userId, boardId, ct);
        foreach (var card in cards)
        {
            card.DetachAssignment(userId);
            await unitOfWork.AuditLogs.AddAsync(new AuditLog("card", card.Id, AuditAction.Updated, actorId,
                JsonSerializer.Serialize(new { reason, removedUserId = userId })), ct);
        }
        return cards;
    }

    public Task NotifyAsync(Guid boardId, Guid cardId, CancellationToken ct = default)
        => _notifier.NotifyBoardMutationAsync(new BoardRealtimeEvent(boardId, "card", "assignments", cardId, DateTimeOffset.UtcNow), ct);
}
