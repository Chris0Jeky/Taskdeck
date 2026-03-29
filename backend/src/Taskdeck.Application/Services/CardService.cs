using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class CardService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBoardRealtimeNotifier _realtimeNotifier;
    private readonly IHistoryService? _historyService;

    public CardService(IUnitOfWork unitOfWork)
        : this(unitOfWork, realtimeNotifier: null, historyService: null)
    {
    }

    public CardService(IUnitOfWork unitOfWork, IBoardRealtimeNotifier? realtimeNotifier = null, IHistoryService? historyService = null)
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

    public async Task<Result<CardDto>> CreateCardAsync(CreateCardDto dto, CancellationToken cancellationToken = default)
    {
        return await CreateCardAsync(dto, cardId: null, cancellationToken);
    }

    public async Task<Result<CardDto>> CreateCardAsync(
        CreateCardDto dto,
        Guid? cardId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Verify board and column exist
            var board = await _unitOfWork.Boards.GetByIdAsync(dto.BoardId, cancellationToken);
            if (board == null)
                return Result.Failure<CardDto>(ErrorCodes.NotFound, $"Board with ID {dto.BoardId} not found");

            var column = await _unitOfWork.Columns.GetByIdWithCardsAsync(dto.ColumnId, cancellationToken);
            if (column == null)
                return Result.Failure<CardDto>(ErrorCodes.NotFound, $"Column with ID {dto.ColumnId} not found");

            // Check WIP limit
            if (column.WouldExceedWipLimitIfAdded())
                return Result.Failure<CardDto>(ErrorCodes.WipLimitExceeded,
                    $"Cannot add card, column '{column.Name}' has reached its WIP limit of {column.WipLimit}");

            // Determine position (add to bottom)
            var position = column.Cards.Any() ? column.Cards.Max(c => c.Position) + 1 : 0;

            var card = cardId.HasValue
                ? new Card(cardId.Value, dto.BoardId, dto.ColumnId, dto.Title, dto.Description, dto.DueDate, position)
                : new Card(dto.BoardId, dto.ColumnId, dto.Title, dto.Description, dto.DueDate, position);
            await _unitOfWork.Cards.AddAsync(card, cancellationToken);

            // Add labels if provided
            if (dto.LabelIds != null && dto.LabelIds.Any())
            {
                var labels = await _unitOfWork.Labels.GetByBoardIdAsync(dto.BoardId, cancellationToken);
                var validLabelIds = labels.Select(l => l.Id).ToHashSet();

                foreach (var labelId in dto.LabelIds.Where(validLabelIds.Contains))
                {
                    var cardLabel = new CardLabel(card.Id, labelId);
                    card.AddLabel(cardLabel);
                }
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _realtimeNotifier.NotifyBoardMutationAsync(
                new BoardRealtimeEvent(card.BoardId, "card", "created", card.Id, DateTimeOffset.UtcNow),
                cancellationToken);
            await SafeLogAsync("card", card.Id, AuditAction.Created, changes: $"title={card.Title}");

            var createdCard = await _unitOfWork.Cards.GetByIdWithLabelsAsync(card.Id, cancellationToken);
            return Result.Success(MapToDto(createdCard!));
        }
        catch (DomainException ex)
        {
            return Result.Failure<CardDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<CardDto>> UpdateCardAsync(
        Guid id,
        UpdateCardDto dto,
        Guid? actorUserId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var card = await _unitOfWork.Cards.GetByIdWithLabelsAsync(id, cancellationToken);
            if (card == null)
                return Result.Failure<CardDto>(ErrorCodes.NotFound, $"Card with ID {id} not found");
            if (dto.ExpectedUpdatedAt.HasValue && dto.ExpectedUpdatedAt.Value != card.UpdatedAt)
            {
                await LogUpdateConflictAsync(card, dto.ExpectedUpdatedAt.Value, actorUserId, cancellationToken);
                return Result.Failure<CardDto>(
                    ErrorCodes.Conflict,
                    "Card was updated by another session. Refresh and retry your changes.");
            }

            // Capture pre-mutation state for change summary
            var oldTitle = card.Title;
            var oldDescription = card.Description;
            var oldDueDate = card.DueDate;
            var oldIsBlocked = card.IsBlocked;
            var oldBlockReason = card.BlockReason;
            var oldLabelIds = card.CardLabels.Select(cl => cl.LabelId).OrderBy(id => id).ToList();

            // Update basic fields
            if (dto.Title != null || dto.Description != null || dto.DueDate.HasValue)
                card.Update(dto.Title, dto.Description, dto.DueDate);

            // Update blocked status
            if (dto.IsBlocked.HasValue)
            {
                if (dto.IsBlocked.Value && !string.IsNullOrEmpty(dto.BlockReason))
                    card.Block(dto.BlockReason);
                else if (!dto.IsBlocked.Value)
                    card.Unblock();
            }

            // Update labels
            if (dto.LabelIds != null)
            {
                card.ClearLabels();
                var labels = await _unitOfWork.Labels.GetByBoardIdAsync(card.BoardId, cancellationToken);
                var validLabelIds = labels.Select(l => l.Id).ToHashSet();

                foreach (var labelId in dto.LabelIds.Where(validLabelIds.Contains))
                {
                    var cardLabel = new CardLabel(card.Id, labelId);
                    card.AddLabel(cardLabel);
                }
            }

            var changeSummary = BuildCardChangeSummary(dto, oldTitle, oldDescription, oldDueDate, oldIsBlocked, oldBlockReason, oldLabelIds);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _realtimeNotifier.NotifyBoardMutationAsync(
                new BoardRealtimeEvent(card.BoardId, "card", "updated", card.Id, DateTimeOffset.UtcNow),
                cancellationToken);
            await SafeLogAsync("card", card.Id, AuditAction.Updated, actorUserId, changeSummary);

            var updatedCard = await _unitOfWork.Cards.GetByIdWithLabelsAsync(id, cancellationToken);
            return Result.Success(MapToDto(updatedCard!));
        }
        catch (DomainException ex)
        {
            return Result.Failure<CardDto>(ex.ErrorCode, ex.Message);
        }
    }

    private static string BuildCardChangeSummary(
        UpdateCardDto dto,
        string oldTitle,
        string? oldDescription,
        DateTimeOffset? oldDueDate,
        bool oldIsBlocked,
        string? oldBlockReason,
        List<Guid> oldLabelIds)
    {
        var parts = new List<string>();
        if (dto.Title != null)
            parts.Add($"Title: '{oldTitle}' -> '{dto.Title}'");
        if (dto.Description != null)
            parts.Add("Description changed");
        if (dto.DueDate.HasValue)
            parts.Add($"DueDate: '{oldDueDate?.ToString("O") ?? "none"}' -> '{dto.DueDate.Value:O}'");
        if (dto.IsBlocked.HasValue && dto.IsBlocked.Value != oldIsBlocked)
            parts.Add(dto.IsBlocked.Value ? $"Blocked: {dto.BlockReason ?? "no reason"}" : "Unblocked");
        if (dto.LabelIds != null)
        {
            var newLabelIds = dto.LabelIds.OrderBy(id => id).ToList();
            if (!oldLabelIds.SequenceEqual(newLabelIds))
                parts.Add($"Labels changed: {oldLabelIds.Count} -> {newLabelIds.Count}");
        }
        return parts.Count > 0 ? string.Join("; ", parts) : "no fields changed";
    }

    public Task<Result<CardDto>> UpdateCardAsync(
        Guid id,
        UpdateCardDto dto,
        CancellationToken cancellationToken)
    {
        return UpdateCardAsync(id, dto, actorUserId: null, cancellationToken);
    }

    public async Task<Result<CardDto>> UpdateCardAsync(
        Guid boardId,
        Guid id,
        UpdateCardDto dto,
        Guid? actorUserId = null,
        CancellationToken cancellationToken = default)
    {
        var card = await _unitOfWork.Cards.GetByIdAsync(id, cancellationToken);
        if (card == null || card.BoardId != boardId)
            return Result.Failure<CardDto>(ErrorCodes.NotFound, $"Card with ID {id} not found in board {boardId}");

        return await UpdateCardAsync(id, dto, actorUserId, cancellationToken);
    }

    public Task<Result<CardDto>> UpdateCardAsync(
        Guid boardId,
        Guid id,
        UpdateCardDto dto,
        CancellationToken cancellationToken)
    {
        return UpdateCardAsync(boardId, id, dto, actorUserId: null, cancellationToken);
    }

    public async Task<Result<CardDto>> MoveCardAsync(Guid id, MoveCardDto dto, CancellationToken cancellationToken = default)
    {
        try
        {
            var card = await _unitOfWork.Cards.GetByIdWithLabelsAsync(id, cancellationToken);
            if (card == null)
                return Result.Failure<CardDto>(ErrorCodes.NotFound, $"Card with ID {id} not found");

            var targetColumn = await _unitOfWork.Columns.GetByIdWithCardsAsync(dto.TargetColumnId, cancellationToken);
            if (targetColumn == null)
                return Result.Failure<CardDto>(ErrorCodes.NotFound, $"Column with ID {dto.TargetColumnId} not found");

            // Check WIP limit (only if moving to a different column)
            if (card.ColumnId != dto.TargetColumnId && targetColumn.WouldExceedWipLimitIfAdded())
                return Result.Failure<CardDto>(ErrorCodes.WipLimitExceeded,
                    $"Cannot move card, target column '{targetColumn.Name}' has reached its WIP limit of {targetColumn.WipLimit}");

            // Move card
            card.MoveToColumn(dto.TargetColumnId, dto.TargetPosition);

            // Reorder other cards in target column
            var cardsInTargetColumn = await _unitOfWork.Cards.GetByColumnIdAsync(dto.TargetColumnId, cancellationToken);
            var orderedCards = cardsInTargetColumn
                .Where(c => c.Id != card.Id)
                .OrderBy(c => c.Position)
                .ToList();

            orderedCards.Insert(dto.TargetPosition, card);

            for (int i = 0; i < orderedCards.Count; i++)
            {
                orderedCards[i].SetPosition(i);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _realtimeNotifier.NotifyBoardMutationAsync(
                new BoardRealtimeEvent(card.BoardId, "card", "moved", card.Id, DateTimeOffset.UtcNow),
                cancellationToken);
            await SafeLogAsync("card", card.Id, AuditAction.Moved, changes: $"target_column={dto.TargetColumnId}; position={dto.TargetPosition}");

            var movedCard = await _unitOfWork.Cards.GetByIdWithLabelsAsync(id, cancellationToken);
            return Result.Success(MapToDto(movedCard!));
        }
        catch (DomainException ex)
        {
            return Result.Failure<CardDto>(ex.ErrorCode, ex.Message);
        }
    }

    public async Task<Result<CardDto>> MoveCardAsync(Guid boardId, Guid id, MoveCardDto dto, CancellationToken cancellationToken = default)
    {
        var card = await _unitOfWork.Cards.GetByIdAsync(id, cancellationToken);
        if (card == null || card.BoardId != boardId)
            return Result.Failure<CardDto>(ErrorCodes.NotFound, $"Card with ID {id} not found in board {boardId}");

        var targetColumn = await _unitOfWork.Columns.GetByIdAsync(dto.TargetColumnId, cancellationToken);
        if (targetColumn == null || targetColumn.BoardId != boardId)
            return Result.Failure<CardDto>(ErrorCodes.NotFound, $"Column with ID {dto.TargetColumnId} not found in board {boardId}");

        return await MoveCardAsync(id, dto, cancellationToken);
    }

    public async Task<Result<IEnumerable<CardDto>>> SearchCardsAsync(
        Guid boardId,
        string? searchText = null,
        Guid? labelId = null,
        Guid? columnId = null,
        CancellationToken cancellationToken = default)
    {
        var cards = await _unitOfWork.Cards.SearchAsync(boardId, searchText, labelId, columnId, cancellationToken);
        return Result.Success(cards.Select(MapToDto));
    }

    public async Task<Result<CardCaptureProvenanceDto>> GetCaptureProvenanceAsync(
        Guid boardId,
        Guid cardId,
        CancellationToken cancellationToken = default)
    {
        var card = await _unitOfWork.Cards.GetByIdAsync(cardId, cancellationToken);
        if (card == null || card.BoardId != boardId)
            return Result.Failure<CardCaptureProvenanceDto>(ErrorCodes.NotFound, $"Card with ID {cardId} not found in board {boardId}");

        var proposal = await _unitOfWork.AutomationProposals.GetLatestByOperationTargetAsync(
            "card",
            cardId.ToString(),
            actionType: "create",
            sourceType: ProposalSourceType.Queue,
            cancellationToken);
        if (proposal == null || string.IsNullOrWhiteSpace(proposal.SourceReferenceId))
        {
            return Result.Failure<CardCaptureProvenanceDto>(
                ErrorCodes.NotFound,
                $"Capture provenance not found for card {cardId}");
        }

        if (!Guid.TryParse(proposal.SourceReferenceId, out var captureItemId))
        {
            return Result.Failure<CardCaptureProvenanceDto>(
                ErrorCodes.NotFound,
                $"Capture provenance not found for card {cardId}");
        }

        var triageRunId = Guid.TryParse(proposal.CorrelationId, out var parsedTriageRunId)
            ? parsedTriageRunId
            : (Guid?)null;

        return Result.Success(new CardCaptureProvenanceDto(
            card.Id,
            captureItemId,
            proposal.Id,
            proposal.Status,
            triageRunId));
    }

    public async Task<Result> DeleteCardAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var card = await _unitOfWork.Cards.GetByIdAsync(id, cancellationToken);
        if (card == null)
            return Result.Failure(ErrorCodes.NotFound, $"Card with ID {id} not found");

        await _unitOfWork.Cards.DeleteAsync(card, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _realtimeNotifier.NotifyBoardMutationAsync(
            new BoardRealtimeEvent(card.BoardId, "card", "deleted", card.Id, DateTimeOffset.UtcNow),
            cancellationToken);
        await SafeLogAsync("card", card.Id, AuditAction.Deleted, changes: $"title={card.Title}");

        return Result.Success();
    }

    public async Task<Result> DeleteCardAsync(Guid boardId, Guid id, CancellationToken cancellationToken = default)
    {
        var card = await _unitOfWork.Cards.GetByIdAsync(id, cancellationToken);
        if (card == null || card.BoardId != boardId)
            return Result.Failure(ErrorCodes.NotFound, $"Card with ID {id} not found in board {boardId}");

        return await DeleteCardAsync(id, cancellationToken);
    }

    private static CardDto MapToDto(Card card)
    {
        var labels = card.CardLabels
            .Select(cl => new LabelDto(
                cl.Label.Id,
                cl.Label.BoardId,
                cl.Label.Name,
                cl.Label.ColorHex,
                cl.Label.CreatedAt,
                cl.Label.UpdatedAt
            ))
            .ToList();

        return new CardDto(
            card.Id,
            card.BoardId,
            card.ColumnId,
            card.Title,
            card.Description,
            card.DueDate,
            card.IsBlocked,
            card.BlockReason,
            card.Position,
            labels,
            card.CreatedAt,
            card.UpdatedAt
        );
    }

    private async Task LogUpdateConflictAsync(
        Card card,
        DateTimeOffset expectedUpdatedAt,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var auditLog = new AuditLog(
            "card",
            card.Id,
            AuditAction.Updated,
            actorUserId,
            $"update_conflict expected_updated_at={expectedUpdatedAt:O}; actual_updated_at={card.UpdatedAt:O}");

        await _unitOfWork.AuditLogs.AddAsync(auditLog, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
