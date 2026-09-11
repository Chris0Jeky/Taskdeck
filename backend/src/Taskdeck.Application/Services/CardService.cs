using Microsoft.Extensions.Logging;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public partial class CardService
{
    private const string ArchivedBoardWriteMessage = "Cannot modify cards on an archived board. Restore the board before editing.";
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBoardRealtimeNotifier _realtimeNotifier;
    private readonly IHistoryService? _historyService;
    private readonly ILogger<CardService>? _logger;

    public CardService(IUnitOfWork unitOfWork)
        : this(unitOfWork, realtimeNotifier: null, historyService: null)
    {
    }

    public CardService(IUnitOfWork unitOfWork, IBoardRealtimeNotifier? realtimeNotifier = null, IHistoryService? historyService = null, ILogger<CardService>? logger = null)
    {
        _unitOfWork = unitOfWork;
        _realtimeNotifier = realtimeNotifier ?? NoOpBoardRealtimeNotifier.Instance;
        _historyService = historyService;
        _logger = logger;
    }

    private Task SafeLogAsync(string entityType, Guid entityId, AuditAction action, Guid? userId = null, string? changes = null)
        => AuditLogWriter.SafeLogAsync(_historyService, _logger, entityType, entityId, action, userId, changes);

    public async Task<Result<IEnumerable<CardDto>>> GetArchivedCardsAsync(Guid boardId, CancellationToken cancellationToken = default)
    {
        var cards = await _unitOfWork.Cards.GetArchivedByBoardIdAsync(boardId, cancellationToken);
        return Result.Success(cards.Select(MapToDto));
    }

    public async Task<Result<CardDto>> GetCardAsync(Guid boardId, Guid cardId, CancellationToken cancellationToken = default)
    {
        var card = await _unitOfWork.Cards.GetByIdWithLabelsAsync(cardId, cancellationToken);
        return card == null || card.BoardId != boardId
            ? Result.Failure<CardDto>(ErrorCodes.NotFound, "Card not found in this board")
            : Result.Success(MapToDto(card));
    }

    /// <summary>
    /// Flips a card's archive state, preserving its original placement.
    /// <para>
    /// <paramref name="recordLifecycleAudit"/> is false only on the proposal apply lane, where
    /// <see cref="Pipeline.ExecutionAuditRecorder"/> writes the single Archived/Unarchived receipt
    /// so it can carry the proposal's provenance. Direct API archive/restore calls leave it true
    /// and keep the actor-stamped receipt staged here, so each lane produces exactly one entry.
    /// </para>
    /// <para>
    /// <paramref name="notificationSink"/> is supplied only on the proposal apply lane (#2934),
    /// where this method's <c>SaveChangesAsync</c> is still inside the executor's outer
    /// transaction: the lifecycle and detached-child events are staged in the sink and published
    /// by the executor after it commits, so a later operation that rolls the transaction back
    /// emits nothing. Direct API calls leave it null and notify immediately, as before.
    /// </para>
    /// </summary>
    public async Task<Result<CardDto>> SetArchivedAsync(Guid boardId, Guid cardId, bool archive,
        CardLifecycleDto dto, Guid? actorUserId = null, bool recordLifecycleAudit = true,
        IBoardRealtimeNotifier? notificationSink = null,
        CancellationToken cancellationToken = default)
    {
        var notifier = notificationSink ?? _realtimeNotifier;
        try
        {
            if (!dto.ExpectedUpdatedAt.HasValue)
                return Result.Failure<CardDto>(ErrorCodes.ValidationError, "ExpectedUpdatedAt is required. Refresh the card before changing its archive state.");
            var card = await _unitOfWork.Cards.GetByIdWithLabelsAsync(cardId, cancellationToken);
            if (card == null || card.BoardId != boardId)
                return Result.Failure<CardDto>(ErrorCodes.NotFound, "Card not found in this board");
            var board = await _unitOfWork.Boards.GetByIdAsync(boardId, cancellationToken);
            if (board == null)
                return Result.Failure<CardDto>(ErrorCodes.NotFound, "Board not found");
            if (board.IsArchived)
                return Result.Failure<CardDto>(ErrorCodes.InvalidOperation, ArchivedBoardWriteMessage);
            if (card.UpdatedAt != dto.ExpectedUpdatedAt.Value)
                return Result.Failure<CardDto>(ErrorCodes.Conflict, "Card changed since it was displayed. Refresh and retry.");
            if (card.IsArchived == archive)
                return Result.Failure<CardDto>(ErrorCodes.InvalidOperation, archive ? "Card is already archived." : "Card is already active.");
            IReadOnlyList<Card> detachedChildren = [];
            if (!archive)
            {
                var column = await _unitOfWork.Columns.GetByIdWithCardsAsync(card.ColumnId, cancellationToken);
                if (column == null || column.BoardId != boardId)
                    return Result.Failure<CardDto>(ErrorCodes.InvalidOperation, "The original column is unavailable. Restore that column before restoring this card.");
                if (column.WouldExceedWipLimitIfAdded())
                    return Result.Failure<CardDto>(ErrorCodes.WipLimitExceeded, "The original column is full. Free space or adjust its WIP limit, then retry restoring this card.");
                card.Restore();
            }
            else
            {
                var children = await ReadDetachChildrenAsync(card, cancellationToken);
                detachedChildren = children;
                var confirmed = ValidateDetachConfirmation(card, children, dto);
                if (!confirmed.IsSuccess) return Result.Failure<CardDto>(confirmed.ErrorCode, confirmed.ErrorMessage);
                await StageDetachChildrenAsync(card, children, actorUserId, cancellationToken);
                card.Archive();
            }
            await _unitOfWork.Cards.StageDependencyProjectionInvalidationAsync(boardId, cancellationToken);
            board.RecordHierarchyMutation();
            if (recordLifecycleAudit)
                await _unitOfWork.AuditLogs.AddAsync(new AuditLog("card", card.Id,
                    archive ? AuditAction.Archived : AuditAction.Unarchived, actorUserId,
                    archive ? "Card archived; original placement retained" : "Card restored to original column"), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await notifier.NotifyBoardMutationAsync(new BoardRealtimeEvent(boardId, "card",
                archive ? "archived" : "restored", card.Id, DateTimeOffset.UtcNow), cancellationToken);
            await NotifyDetachedChildrenAsync(boardId, detachedChildren, cancellationToken, notifier);
            return Result.Success(MapToDto(card));
        }
        catch (DomainException ex) { return Result.Failure<CardDto>(ex.ErrorCode, ex.Message); }
    }

    public async Task<Result<CardDto>> CreateCardAsync(CreateCardDto dto, CancellationToken cancellationToken = default)
    {
        return await CreateCardAsync(dto, cardId: null, actorUserId: null, cancellationToken);
    }

    /// <summary>
    /// Proposal-lane entry point: the apply pipeline supplies a pre-allocated card id and no
    /// human actor, so the audit row stays unattributed (the proposal's own provenance carries
    /// the attribution). Direct user CRUD calls the actor-carrying overload below.
    /// </summary>
    public async Task<Result<CardDto>> CreateCardAsync(
        CreateCardDto dto,
        Guid? cardId,
        CancellationToken cancellationToken = default)
    {
        return await CreateCardAsync(dto, cardId, actorUserId: null, cancellationToken);
    }

    public async Task<Result<CardDto>> CreateCardAsync(
        CreateCardDto dto,
        Guid? cardId,
        Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var staged = await StageCardCreationAsync(dto, cardId, cancellationToken);
            if (!staged.IsSuccess) return Result.Failure<CardDto>(staged.ErrorCode, staged.ErrorMessage);
            var card = staged.Value;
            if (dto.ParentCardId.HasValue)
                await _unitOfWork.AuditLogs.AddAsync(new AuditLog("card", card.Id, AuditAction.Created, actorUserId,
                    $"title={card.Title}; WorkItemType={card.WorkItemType}; ParentCardId={card.ParentCardId}"), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _realtimeNotifier.NotifyBoardMutationAsync(
                new BoardRealtimeEvent(card.BoardId, "card", "created", card.Id, DateTimeOffset.UtcNow),
                cancellationToken);
            if (!dto.ParentCardId.HasValue) await SafeLogAsync("card", card.Id, AuditAction.Created, actorUserId, $"title={card.Title}; WorkItemType={card.WorkItemType}");

            var createdCard = await _unitOfWork.Cards.GetByIdWithLabelsAsync(card.Id, cancellationToken);
            return Result.Success(MapToDto(createdCard!));
        }
        catch (DomainException ex)
        {
            return Result.Failure<CardDto>(ex.ErrorCode, ex.Message);
        }
    }

    // Shared guarded writer. The caller must atomically commit staged entities, audit and
    // links before publishing realtime. This method performs no save or notification.
    internal async Task<Result<Card>> StageCardCreationAsync(
        CreateCardDto dto, Guid? cardId, CancellationToken cancellationToken)
    {
        try
        {
            var workItemType = Card.ParseWorkItemType(dto.WorkItemType ?? "Task");

            // Verify board and column exist
            var board = await _unitOfWork.Boards.GetByIdAsync(dto.BoardId, cancellationToken);
            if (board == null)
                return Result.Failure<Card>(ErrorCodes.NotFound, $"Board with ID {dto.BoardId} not found");
            if (board.IsArchived)
                return Result.Failure<Card>(ErrorCodes.InvalidOperation, ArchivedBoardWriteMessage);

            var column = await _unitOfWork.Columns.GetByIdWithCardsAsync(dto.ColumnId, cancellationToken);
            if (column == null)
                return Result.Failure<Card>(ErrorCodes.NotFound, $"Column with ID {dto.ColumnId} not found");

            if (column.BoardId != dto.BoardId)
                return Result.Failure<Card>(ErrorCodes.NotFound, $"Column with ID {dto.ColumnId} not found in board {dto.BoardId}");

            // Check WIP limit
            if (column.WouldExceedWipLimitIfAdded())
                return Result.Failure<Card>(ErrorCodes.WipLimitExceeded,
                    $"Cannot add card, column '{column.Name}' has reached its WIP limit of {column.WipLimit}");

            // Determine position (add to bottom)
            var position = column.Cards.Any() ? column.Cards.Max(c => c.Position) + 1 : 0;

            var card = cardId.HasValue
                ? new Card(cardId.Value, dto.BoardId, dto.ColumnId, dto.Title, dto.Description, dto.DueDate, position)
                : new Card(dto.BoardId, dto.ColumnId, dto.Title, dto.Description, dto.DueDate, position);
            card.SetWorkItemType(workItemType);
            if (dto.ParentCardId.HasValue)
            {
                var graph = await _unitOfWork.Cards.GetHierarchyByBoardIdAsync(dto.BoardId, cancellationToken);
                ValidateActiveParent(graph, dto.ParentCardId);
                CardHierarchy.ValidateParent(graph.Append(card), card.Id, dto.ParentCardId);
                card.SetParent(dto.ParentCardId);
            }
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

            if (dto.ParentCardId.HasValue) board.RecordHierarchyMutation();
            else board.RecordCardMutation();
            return Result.Success(card);
        }
        catch (DomainException ex) { return Result.Failure<Card>(ex.ErrorCode, ex.Message); }
    }

    public async Task<Result<CardDto>> UpdateCardAsync(
        Guid id,
        UpdateCardDto dto,
        Guid? actorUserId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var changesParent = dto.ParentCardId.HasValue || dto.ClearParent;
            if (dto.ParentCardId.HasValue && dto.ClearParent)
                return Result.Failure<CardDto>(ErrorCodes.ValidationError, "ParentCardId and ClearParent cannot both be set.");
            if (changesParent && !dto.ExpectedUpdatedAt.HasValue)
                return Result.Failure<CardDto>(ErrorCodes.ValidationError, "ExpectedUpdatedAt is required when changing parent.");
            var workItemType = dto.WorkItemType is null ? (CardWorkItemType?)null : Card.ParseWorkItemType(dto.WorkItemType);
            if (workItemType.HasValue && !dto.ExpectedUpdatedAt.HasValue)
                return Result.Failure<CardDto>(ErrorCodes.ValidationError, "ExpectedUpdatedAt is required when changing WorkItemType. Refresh the card first.");
            if (dto.ClearDueDate && dto.DueDate.HasValue)
                return Result.Failure<CardDto>(ErrorCodes.ValidationError, "DueDate and ClearDueDate cannot both be set");

            var card = await _unitOfWork.Cards.GetByIdWithLabelsAsync(id, cancellationToken);
            if (card == null)
                return Result.Failure<CardDto>(ErrorCodes.NotFound, $"Card with ID {id} not found");

            if (card.IsArchived)
                return Result.Failure<CardDto>(ErrorCodes.InvalidOperation, "Card is archived. Restore the card before editing.");

            var board = await _unitOfWork.Boards.GetByIdAsync(card.BoardId, cancellationToken);
            if (board?.IsArchived == true)
                return Result.Failure<CardDto>(ErrorCodes.InvalidOperation, ArchivedBoardWriteMessage);

            if (dto.ExpectedUpdatedAt.HasValue && dto.ExpectedUpdatedAt.Value != card.UpdatedAt)
            {
                await LogUpdateConflictAsync(card, dto.ExpectedUpdatedAt.Value, actorUserId, cancellationToken);
                return Result.Failure<CardDto>(
                    ErrorCodes.Conflict,
                    "Card was updated by another session. Refresh and retry your changes.");
            }

            var oldParentId = card.ParentCardId;
            if (changesParent)
            {
                var graph = await _unitOfWork.Cards.GetHierarchyByBoardIdAsync(card.BoardId, cancellationToken);
                ValidateActiveParent(graph, dto.ParentCardId);
                CardHierarchy.ValidateParent(graph, card.Id, dto.ParentCardId);
                card.SetParent(dto.ParentCardId);
            }
            // Capture pre-mutation state for change summary
            var oldWorkItemType = card.WorkItemType;
            var oldTitle = card.Title;
            var oldDescription = card.Description;
            var oldDueDate = card.DueDate;
            var oldIsBlocked = card.IsBlocked;
            var oldBlockReason = card.BlockReason;
            var oldLabelIds = card.CardLabels.Select(cl => cl.LabelId).OrderBy(id => id).ToList();

            if (workItemType.HasValue) card.SetWorkItemType(workItemType.Value);

            // Update basic fields
            if (dto.Title != null || dto.Description != null || dto.DueDate.HasValue)
                card.Update(dto.Title, dto.Description, dto.DueDate);
            if (dto.ClearDueDate)
                card.ClearDueDate();

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
            if (workItemType.HasValue && oldWorkItemType != workItemType.Value)
                changeSummary = $"WorkItemType: {oldWorkItemType} -> {workItemType.Value}" +
                    (changeSummary == "no fields changed" ? "" : $"; {changeSummary}");

            if (changesParent)
            {
                changeSummary = $"ParentCardId: {oldParentId?.ToString() ?? "none"} -> {card.ParentCardId?.ToString() ?? "none"}; {changeSummary}";
                board?.RecordHierarchyMutation();
                await _unitOfWork.AuditLogs.AddAsync(new AuditLog("card", card.Id, AuditAction.Updated, actorUserId, changeSummary), cancellationToken);
            }
            else board?.RecordCardMutation();
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _realtimeNotifier.NotifyBoardMutationAsync(
                new BoardRealtimeEvent(card.BoardId, "card", "updated", card.Id, DateTimeOffset.UtcNow),
                cancellationToken);
            if (!changesParent) await SafeLogAsync("card", card.Id, AuditAction.Updated, actorUserId, changeSummary);

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
        if (dto.Title != null && dto.Title != oldTitle)
            parts.Add($"Title: '{oldTitle}' -> '{dto.Title}'");
        if (dto.Description != null && dto.Description != oldDescription)
            parts.Add("Description changed");
        if (dto.ClearDueDate && oldDueDate.HasValue)
            parts.Add($"DueDate: '{oldDueDate.Value:O}' -> 'none'");
        else if (dto.DueDate.HasValue && dto.DueDate.Value != oldDueDate)
            parts.Add($"DueDate: '{oldDueDate?.ToString("O") ?? "none"}' -> '{dto.DueDate.Value:O}'");
        if (dto.IsBlocked.HasValue && dto.IsBlocked.Value != oldIsBlocked)
        {
            if (dto.IsBlocked.Value && !string.IsNullOrEmpty(dto.BlockReason) && !oldIsBlocked)
                parts.Add($"Blocked: {dto.BlockReason}");
            else if (!dto.IsBlocked.Value && oldIsBlocked)
                parts.Add("Unblocked");
        }
        else if (dto.IsBlocked == true &&
                 !string.IsNullOrEmpty(dto.BlockReason) &&
                 !string.Equals(dto.BlockReason, oldBlockReason, StringComparison.Ordinal))
        {
            parts.Add($"Block reason: '{oldBlockReason}' -> '{dto.BlockReason}'");
        }
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

    public Task<Result<CardDto>> MoveCardAsync(Guid id, MoveCardDto dto, CancellationToken cancellationToken)
    {
        return MoveCardAsync(id, dto, actorUserId: null, cancellationToken);
    }

    public async Task<Result<CardDto>> MoveCardAsync(Guid id, MoveCardDto dto, Guid? actorUserId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var card = await _unitOfWork.Cards.GetByIdWithLabelsAsync(id, cancellationToken);
            if (card == null)
                return Result.Failure<CardDto>(ErrorCodes.NotFound, $"Card with ID {id} not found");

            var board = await _unitOfWork.Boards.GetByIdAsync(card.BoardId, cancellationToken);
            if (board?.IsArchived == true)
                return Result.Failure<CardDto>(ErrorCodes.InvalidOperation, ArchivedBoardWriteMessage);

            var targetColumn = await _unitOfWork.Columns.GetByIdWithCardsAsync(dto.TargetColumnId, cancellationToken);
            if (targetColumn == null)
                return Result.Failure<CardDto>(ErrorCodes.NotFound, $"Column with ID {dto.TargetColumnId} not found");

            var targetBoard = await _unitOfWork.Boards.GetByIdAsync(targetColumn.BoardId, cancellationToken);
            if (targetBoard?.IsArchived == true)
                return Result.Failure<CardDto>(ErrorCodes.InvalidOperation, ArchivedBoardWriteMessage);

            if (targetColumn.BoardId != card.BoardId)
                return Result.Failure<CardDto>(ErrorCodes.NotFound, $"Column with ID {dto.TargetColumnId} not found in board {card.BoardId}");

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

            // Insert at the requested index, clamped to the end when the request overshoots -
            // the same idiom as ColumnService.ReorderColumnAsync. A request can legitimately
            // overshoot whenever the column's stored positions are non-contiguous (#3025: a
            // deleted middle card leaves 0 and 2, and an append index derived from max(Position)
            // is then 3 on a two-card list). Negative positions are still refused, by
            // Card.SetPosition above, before this line is reached.
            orderedCards.Insert(Math.Min(dto.TargetPosition, orderedCards.Count), card);

            for (int i = 0; i < orderedCards.Count; i++)
            {
                orderedCards[i].SetPosition(i);
            }

            board?.RecordCardMutation();
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _realtimeNotifier.NotifyBoardMutationAsync(
                new BoardRealtimeEvent(card.BoardId, "card", "moved", card.Id, DateTimeOffset.UtcNow),
                cancellationToken);
            await SafeLogAsync("card", card.Id, AuditAction.Moved, actorUserId, $"target_column={dto.TargetColumnId}; position={dto.TargetPosition}");

            var movedCard = await _unitOfWork.Cards.GetByIdWithLabelsAsync(id, cancellationToken);
            return Result.Success(MapToDto(movedCard!));
        }
        catch (DomainException ex)
        {
            return Result.Failure<CardDto>(ex.ErrorCode, ex.Message);
        }
    }

    public Task<Result<CardDto>> MoveCardAsync(Guid boardId, Guid id, MoveCardDto dto, CancellationToken cancellationToken)
    {
        return MoveCardAsync(boardId, id, dto, actorUserId: null, cancellationToken);
    }

    public async Task<Result<CardDto>> MoveCardAsync(Guid boardId, Guid id, MoveCardDto dto, Guid? actorUserId = null, CancellationToken cancellationToken = default)
    {
        var card = await _unitOfWork.Cards.GetByIdAsync(id, cancellationToken);
        if (card == null || card.BoardId != boardId)
            return Result.Failure<CardDto>(ErrorCodes.NotFound, $"Card with ID {id} not found in board {boardId}");

        var targetColumn = await _unitOfWork.Columns.GetByIdAsync(dto.TargetColumnId, cancellationToken);
        if (targetColumn == null || targetColumn.BoardId != boardId)
            return Result.Failure<CardDto>(ErrorCodes.NotFound, $"Column with ID {dto.TargetColumnId} not found in board {boardId}");

        return await MoveCardAsync(id, dto, actorUserId, cancellationToken);
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

    public Task<Result> DeleteCardAsync(Guid id, CancellationToken cancellationToken)
    {
        return DeleteCardAsync(id, actorUserId: null, cancellationToken);
    }

    public async Task<Result> DeleteCardAsync(Guid id, Guid? actorUserId = null, CancellationToken cancellationToken = default, CardLifecycleDto? confirmation = null)
    {
        try
        {
            var card = await _unitOfWork.Cards.GetByIdAsync(id, cancellationToken);
            if (card == null)
                return Result.Failure(ErrorCodes.NotFound, $"Card with ID {id} not found");

            var board = await _unitOfWork.Boards.GetByIdAsync(card.BoardId, cancellationToken);
            if (board?.IsArchived == true)
                return Result.Failure(ErrorCodes.InvalidOperation, ArchivedBoardWriteMessage);

            var children = await ReadDetachChildrenAsync(card, cancellationToken);
            var confirmed = ValidateDetachConfirmation(card, children, confirmation);
            if (!confirmed.IsSuccess) return confirmed;
            await StageDetachChildrenAsync(card, children, actorUserId, cancellationToken);
            await _unitOfWork.Cards.DeleteAsync(card, cancellationToken);
            board?.RecordHierarchyMutation();
            await _unitOfWork.AuditLogs.AddAsync(new AuditLog("card", card.Id, AuditAction.Deleted, actorUserId, $"title={card.Title}"), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _realtimeNotifier.NotifyBoardMutationAsync(
                new BoardRealtimeEvent(card.BoardId, "card", "deleted", card.Id, DateTimeOffset.UtcNow),
                cancellationToken);
            await NotifyDetachedChildrenAsync(card.BoardId, children, cancellationToken);


            return Result.Success();
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.ErrorCode, ex.Message);
        }
    }

    public Task<Result> DeleteCardAsync(Guid boardId, Guid id, CancellationToken cancellationToken)
    {
        return DeleteCardAsync(boardId, id, actorUserId: null, cancellationToken);
    }

    public async Task<Result> DeleteCardAsync(Guid boardId, Guid id, Guid? actorUserId = null, CancellationToken cancellationToken = default, CardLifecycleDto? confirmation = null)
    {
        var card = await _unitOfWork.Cards.GetByIdAsync(id, cancellationToken);
        if (card == null || card.BoardId != boardId)
            return Result.Failure(ErrorCodes.NotFound, $"Card with ID {id} not found in board {boardId}");

        return await DeleteCardAsync(id, actorUserId, cancellationToken, confirmation);
    }

    internal static CardDto MapToDto(Card card)
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
            card.UpdatedAt,
            card.IsArchived,
            card.WorkItemType.ToString(),
            card.ParentCardId,
            card.Assignments.OrderBy(a => a.UserId).Select(a => new CardAssignmentDto(
                a.UserId, a.User?.Username ?? "Participant", a.AssignedAt, a.AssignedByUserId)).ToArray()
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
