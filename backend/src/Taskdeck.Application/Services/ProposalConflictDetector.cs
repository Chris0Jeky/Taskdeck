using Microsoft.Extensions.Logging;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services.Pipeline;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

/// <summary>
/// Analyzes a proposal and produces tone-classified conflict/warning/status rows
/// for the review UI (section IV: Conflicts and warnings).
/// </summary>
public class ProposalConflictDetector : IProposalConflictDetector
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuthorizationService _authorizationService;
    private readonly IRelatedProposalEvidenceService _relatedEvidence;
    private readonly ILogger<ProposalConflictDetector>? _logger;

    public ProposalConflictDetector(
        IUnitOfWork unitOfWork,
        IAuthorizationService authorizationService,
        IRelatedProposalEvidenceService relatedEvidence,
        ILogger<ProposalConflictDetector>? logger = null)
    {
        _unitOfWork = unitOfWork;
        _authorizationService = authorizationService;
        _relatedEvidence = relatedEvidence
            ?? throw new ArgumentNullException(nameof(relatedEvidence));
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<ConflictRowDto>>> DetectConflictsAsync(
        Guid proposalId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var proposal = await _unitOfWork.AutomationProposals.GetByIdAsync(proposalId, cancellationToken);
        if (proposal is null)
            return Result.Failure<IReadOnlyList<ConflictRowDto>>(ErrorCodes.NotFound, "Proposal not found");

        return await DetectConflictsAsync(ToContext(proposal), userId, cancellationToken);
    }

    public Task<Result<IReadOnlyList<ConflictRowDto>>> DetectConflictsAsync(
        ProposalDto effectiveProposal,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(effectiveProposal);
        return DetectConflictsAsync(ToContext(effectiveProposal), userId, cancellationToken);
    }

    private async Task<Result<IReadOnlyList<ConflictRowDto>>> DetectConflictsAsync(
        ProposalConflictContext proposal,
        Guid userId,
        CancellationToken cancellationToken)
    {

        // Authorization: board-scoped proposals require current board read access,
        // even for the original proposal owner, matching controller-level read paths.
        var authResult = await AuthorizeAccessAsync(proposal, userId, cancellationToken);
        if (!authResult.IsSuccess)
            return Result.Failure<IReadOnlyList<ConflictRowDto>>(authResult.ErrorCode, authResult.ErrorMessage);

        var rows = new List<ConflictRow>();
        var flaggedCardIds = new HashSet<Guid>();
        var flaggedColumnIds = new HashSet<Guid>();
        // Do not echo the validator error: historical parameters can contain private text.
        ProposalOperationContractValidator.ValidateShape(proposal.Operations, out var unevaluatedOperationCount);
        if (unevaluatedOperationCount > 0)
        {
            var operationLabel = unevaluatedOperationCount == 1 ? "operation" : "operations";
            rows.Add(new ConflictRow(
                ConflictTone.Warn,
                "unable-to-evaluate-operation",
                $"{unevaluatedOperationCount} proposal {operationLabel} could not be fully evaluated. Review or recreate the proposal before applying."));
            _logger?.LogWarning(
                "Proposal conflict review incomplete for {ProposalId}; unevaluated_operation_count={UnevaluatedOperationCount}",
                proposal.Id,
                unevaluatedOperationCount);
        }

        // Proposal access does not grant authority over arbitrary referenced IDs.
        // This reader and all its permission decisions live for this request only.
        var targets = new ProposalConflictEntityReader(
            _unitOfWork, _authorizationService, proposal.BoardId, userId);
        var projectedColumnChanges = await GetProjectedColumnChangesAsync(
            proposal, targets, cancellationToken);

        // Check each condition and collect rows
        await CheckStaleDataAsync(proposal, rows, flaggedCardIds, targets, cancellationToken);
        await CheckWipLimitAsync(proposal, rows, flaggedColumnIds, targets,
            projectedColumnChanges, cancellationToken);
        await CheckDuplicatePendingProposalsAsync(proposal, rows, cancellationToken);
        CheckHighRiskOperations(proposal, rows);
        await CheckOutboundWebhooksAsync(proposal, rows, cancellationToken);
        await CheckActiveCommentsAsync(proposal, rows, targets, cancellationToken);
        CheckMultipleOperationsOnSameCard(proposal, rows);

        // If no warnings or info rows, emit an Ok row. An incomplete review must
        // never produce affirmative safety evidence from the operations it skipped.
        if (rows.Count == 0)
        {
            rows.Add(new ConflictRow(ConflictTone.Ok, "status", "No conflicts detected"));
        }
        else if (unevaluatedOperationCount == 0)
        {
            // Add positive signals when applicable
            await AddPositiveSignalsAsync(proposal, rows, flaggedCardIds, flaggedColumnIds,
                targets, projectedColumnChanges, cancellationToken);
        }

        // Sort: Warn first, then Info, then Ok
        var sorted = rows
            .OrderBy(r => r.Tone)
            .ToList();

        return Result.Success<IReadOnlyList<ConflictRowDto>>(
            sorted.Select(ConflictRowDto.FromDomain).ToList());
    }

    private async Task<Result> AuthorizeAccessAsync(
        ProposalConflictContext proposal,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (proposal.BoardId.HasValue)
        {
            var canRead = await _authorizationService.CanReadBoardAsync(userId, proposal.BoardId.Value);
            if (canRead.IsSuccess && canRead.Value)
                return Result.Success();

            return Result.Failure(ErrorCodes.Forbidden, "You do not have permission to view conflicts for this proposal");
        }

        return proposal.RequestedByUserId == userId
            ? Result.Success()
            : Result.Failure(ErrorCodes.Forbidden, "You do not have permission to view conflicts for this proposal");
    }

    /// <summary>
    /// Warn: target card was modified since the proposal was generated.
    /// Compares card's UpdatedAt against proposal's CreatedAt.
    /// Skips create operations since those cards don't exist yet.
    /// </summary>
    private async Task CheckStaleDataAsync(
        ProposalConflictContext proposal,
        List<ConflictRow> rows,
        HashSet<Guid> flaggedCardIds,
        ProposalConflictEntityReader targets,
        CancellationToken cancellationToken)
    {
        var cardTargetIds = GetDistinctCardTargetIds(proposal, includeCreate: false);
        if (cardTargetIds.Count == 0) return;

        foreach (var cardId in cardTargetIds)
        {
            var card = await targets.GetCardAsync(cardId, cancellationToken);
            if (card is null)
            {
                flaggedCardIds.Add(cardId);
                rows.Add(new ConflictRow(
                    ConflictTone.Warn,
                    "missing-target",
                    $"Target card {cardId} no longer exists"));
                continue;
            }

            if (card.UpdatedAt > proposal.CreatedAt)
            {
                flaggedCardIds.Add(cardId);
                rows.Add(new ConflictRow(
                    ConflictTone.Warn,
                    "stale-data",
                    $"Card \"{card.Title}\" was modified after this proposal was generated"));
            }
        }
    }

    /// <summary>
    /// Warn: target column is at or above WIP limit.
    /// Checks the highest occupancy reached by incoming cards in operation order.
    /// </summary>
    private async Task CheckWipLimitAsync(
        ProposalConflictContext proposal,
        List<ConflictRow> rows,
        HashSet<Guid> flaggedColumnIds,
        ProposalConflictEntityReader targets,
        IReadOnlyDictionary<Guid, ColumnProjection> projectedColumnChanges,
        CancellationToken cancellationToken)
    {
        if (projectedColumnChanges.Count == 0) return;

        foreach (var (columnId, projection) in projectedColumnChanges)
        {
            var column = await targets.GetColumnAsync(columnId, cancellationToken);
            if (column is null)
            {
                if (projection.ReceivesCards)
                {
                    flaggedColumnIds.Add(columnId);
                    rows.Add(new ConflictRow(
                        ConflictTone.Warn,
                        "missing-target-column",
                        $"Target column {columnId:N} no longer exists"));
                }

                continue;
            }

            if (!projection.ReceivesCards)
                continue;

            var projectedCount = column.Cards.Count(card => !card.IsArchived) + projection.PeakIncomingDelta;
            if (column.WipLimit.HasValue && projectedCount > column.WipLimit.Value)
            {
                flaggedColumnIds.Add(columnId);
                rows.Add(new ConflictRow(
                    ConflictTone.Warn,
                    "wip-limit",
                    $"Column \"{column.Name}\" would exceed WIP limit ({projectedCount}/{column.WipLimit.Value})"));
            }
        }
    }

    /// <summary>
    /// Warn: another pending proposal targets the same card.
    /// Considers ANY pending proposal on the target card, not just the latest, and
    /// matches on each candidate's EFFECTIVE operation set (latest pending revision
    /// or approved pin) rather than its immutable creation-time rows, so the warning
    /// agrees with what approval and Apply would actually mutate (#2452).
    /// </summary>
    private async Task CheckDuplicatePendingProposalsAsync(
        ProposalConflictContext proposal,
        List<ConflictRow> rows,
        CancellationToken cancellationToken)
    {
        var cardTargetIds = GetDistinctCardTargetIds(proposal, includeCreate: true);
        if (cardTargetIds.Count == 0) return;

        var scope = new ProposalEvidenceScope(proposal.BoardId, proposal.RequestedByUserId);
        foreach (var cardId in cardTargetIds)
        {
            var hasDuplicate = await _relatedEvidence.HasOtherPendingProposalTargetingCardAsync(
                scope, proposal.Id, cardId, cancellationToken);

            if (hasDuplicate)
            {
                rows.Add(new ConflictRow(
                    ConflictTone.Warn,
                    "duplicate-proposal",
                    $"Another pending proposal also targets card {cardId:N}"));
            }
        }
    }

    /// <summary>
    /// Warn: proposal risk level is High or Critical.
    /// </summary>
    private static void CheckHighRiskOperations(
        ProposalConflictContext proposal,
        List<ConflictRow> rows)
    {
        if (proposal.RiskLevel is RiskLevel.High or RiskLevel.Critical)
        {
            rows.Add(new ConflictRow(
                ConflictTone.Warn,
                "high-risk",
                $"Proposal risk level is {proposal.RiskLevel}"));
        }
    }

    /// <summary>
    /// Info: proposal will trigger outbound webhooks.
    /// </summary>
    private async Task CheckOutboundWebhooksAsync(
        ProposalConflictContext proposal,
        List<ConflictRow> rows,
        CancellationToken cancellationToken)
    {
        if (!proposal.BoardId.HasValue) return;

        var eventTypes = GetWebhookEventTypes(proposal);
        if (eventTypes.Count == 0) return;

        var webhooks = await _unitOfWork.OutboundWebhookSubscriptions
            .GetActiveByBoardAsync(proposal.BoardId.Value, cancellationToken);

        var matchingWebhookCount = webhooks
            .Count(webhook => eventTypes.Any(webhook.MatchesEvent));

        if (matchingWebhookCount > 0)
        {
            rows.Add(new ConflictRow(
                ConflictTone.Info,
                "webhooks",
                $"This proposal will trigger {matchingWebhookCount} outbound webhook(s)"));
        }
    }

    /// <summary>
    /// Info: target card has active comments/discussion.
    /// </summary>
    private async Task CheckActiveCommentsAsync(
        ProposalConflictContext proposal,
        List<ConflictRow> rows,
        ProposalConflictEntityReader targets,
        CancellationToken cancellationToken)
    {
        var cardTargetIds = GetDistinctCardTargetIds(proposal, includeCreate: false);
        if (cardTargetIds.Count == 0) return;

        foreach (var cardId in cardTargetIds)
        {
            // Even a count reveals private activity. Missing and hidden cards both
            // stop here; their bounded warning is produced by CheckStaleDataAsync.
            if (await targets.GetCardAsync(cardId, cancellationToken) is null)
                continue;
            var commentCount = await _unitOfWork.CardComments.CountByCardIdAsync(cardId, cancellationToken);
            if (commentCount > 0)
            {
                rows.Add(new ConflictRow(
                    ConflictTone.Info,
                    "active-comments",
                    $"Card {cardId:N} has {commentCount} comment(s)"));
            }
        }
    }

    /// <summary>
    /// Info: multiple operations in the proposal affect the same card.
    /// </summary>
    private static void CheckMultipleOperationsOnSameCard(
        ProposalConflictContext proposal,
        List<ConflictRow> rows)
    {
        var cardOps = proposal.Operations
            .Where(op => op.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase)
                         && !string.IsNullOrEmpty(op.TargetId))
            .GroupBy(op => op.TargetId!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var group in cardOps)
        {
            rows.Add(new ConflictRow(
                ConflictTone.Info,
                "multi-op",
                $"Card {group.Key} is affected by {group.Count()} operations in this proposal"));
        }
    }

    /// <summary>
    /// Add positive Ok signals for target columns with capacity and fresh card data.
    /// Only added when there are already some warn/info rows (otherwise the "no conflicts" row covers it).
    /// Reuses cached entities to avoid redundant DB lookups.
    /// </summary>
    private async Task AddPositiveSignalsAsync(
        ProposalConflictContext proposal,
        List<ConflictRow> rows,
        HashSet<Guid> flaggedCardIds,
        HashSet<Guid> flaggedColumnIds,
        ProposalConflictEntityReader targets,
        IReadOnlyDictionary<Guid, ColumnProjection> projectedColumnChanges,
        CancellationToken cancellationToken)
    {
        // Ok: target column has capacity (only if we didn't already warn about WIP for this column)
        foreach (var (columnId, projection) in projectedColumnChanges)
        {
            if (!projection.ReceivesCards) continue;
            if (flaggedColumnIds.Contains(columnId)) continue;

            var column = await targets.GetColumnAsync(columnId, cancellationToken);
            if (column is null) continue;

            var projectedCount = column.Cards.Count(card => !card.IsArchived) + projection.Delta;
            if (column.WipLimit.HasValue && projectedCount <= column.WipLimit.Value)
            {
                rows.Add(new ConflictRow(
                    ConflictTone.Ok,
                    "capacity",
                    $"Column \"{column.Name}\" has projected capacity ({projectedCount}/{column.WipLimit.Value})"));
            }
        }

        // Ok: card data is fresh (only for cards we didn't already flag as stale/missing)
        var cardTargetIds = GetDistinctCardTargetIds(proposal, includeCreate: false);
        foreach (var cardId in cardTargetIds)
        {
            if (flaggedCardIds.Contains(cardId)) continue;

            var card = await targets.GetCardAsync(cardId, cancellationToken);
            if (card is not null)
            {
                rows.Add(new ConflictRow(
                    ConflictTone.Ok,
                    "fresh-data",
                    $"Card \"{card.Title}\" data is current"));
            }
        }
    }

    /// <summary>
    /// Extracts distinct card GUIDs from proposal operations that target cards.
    /// Excludes create operations since those cards don't exist yet and would
    /// produce false stale/missing warnings.
    /// </summary>
    private static List<Guid> GetDistinctCardTargetIds(ProposalConflictContext proposal, bool includeCreate)
    {
        return proposal.Operations
            .Where(op => op.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase)
                         && (includeCreate || !op.ActionType.Equals("create", StringComparison.OrdinalIgnoreCase)))
            .Select(TryGetCardId)
            .Where(cardId => cardId.HasValue)
            .Select(cardId => cardId!.Value)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Projects active card occupancy in execution order, including lifecycle changes.
    /// Same-column and archived-card moves do not consume or free active capacity.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, ColumnProjection>> GetProjectedColumnChangesAsync(
        ProposalConflictContext proposal,
        ProposalConflictEntityReader targets,
        CancellationToken cancellationToken)
    {
        var changes = new Dictionary<Guid, ColumnProjection>();
        var cardStates = new Dictionary<Guid, (Guid ColumnId, bool IsArchived)>();

        foreach (var op in proposal.Operations.OrderBy(operation => operation.Sequence))
        {
            var action = op.ActionType.ToLowerInvariant();
            if (action is not ("create" or "move" or "archive-lifecycle" or "restore-lifecycle" or "delete"))
                continue;

            var isCard = op.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase);
            var cardId = isCard ? TryGetCardId(op) : null;
            (Guid ColumnId, bool IsArchived)? state = null;
            if (cardId.HasValue && action != "create")
            {
                if (cardStates.TryGetValue(cardId.Value, out var projectedState))
                    state = projectedState;
                else if (await targets.GetCardAsync(cardId.Value, cancellationToken) is { } card)
                    state = (card.ColumnId, card.IsArchived);
            }

            if (action is "archive-lifecycle" or "restore-lifecycle" or "delete")
            {
                if (!state.HasValue)
                    continue;
                var becomesArchived = action != "restore-lifecycle";
                if (state.Value.IsArchived == becomesArchived)
                    continue;
                AddColumnProjectionDelta(changes, state.Value.ColumnId, becomesArchived ? -1 : 1, !becomesArchived);
                cardStates[cardId!.Value] = (state.Value.ColumnId, becomesArchived);
                continue;
            }

            var targetColumnId = TryGetTargetColumnId(op);
            if (!targetColumnId.HasValue ||
                (action == "move" && state.HasValue &&
                 (state.Value.IsArchived || state.Value.ColumnId == targetColumnId.Value)))
                continue;

            // A rejected move cannot free its source slot for a later operation. Still
            // retain the attempted destination count so Review names that WIP violation.
            var targetColumn = await targets.GetColumnAsync(targetColumnId.Value, cancellationToken);
            var moveExceedsCapacity = action == "move" && targetColumn?.WipLimit is { } limit &&
                targetColumn.Cards.Count(card => !card.IsArchived) + changes.GetValueOrDefault(targetColumnId.Value).Delta >= limit;

            if (action == "move" && state.HasValue && !moveExceedsCapacity)
                AddColumnProjectionDelta(changes, state.Value.ColumnId, delta: -1, receivesCards: false);

            AddColumnProjectionDelta(changes, targetColumnId.Value, delta: 1, receivesCards: true);
            if (isCard && cardId.HasValue && !moveExceedsCapacity)
                cardStates[cardId.Value] = (targetColumnId.Value, false);
        }

        return changes;
    }

    private static void AddColumnProjectionDelta(
        Dictionary<Guid, ColumnProjection> changes,
        Guid columnId,
        int delta,
        bool receivesCards)
    {
        var existing = changes.GetValueOrDefault(columnId);
        var projectedDelta = existing.Delta + delta;
        changes[columnId] = new ColumnProjection(
            projectedDelta,
            existing.ReceivesCards || receivesCards,
            receivesCards
                ? existing.ReceivesCards ? Math.Max(existing.PeakIncomingDelta, projectedDelta) : projectedDelta
                : existing.PeakIncomingDelta);
    }

    /// <summary>
    /// Extracts a target column ID from an operation that moves or creates into a column.
    /// Checks TargetType before Parameters so column-targeted operations with no
    /// parameters are still detected. Parses JSON parameters for "columnId" or
    /// "targetColumnId" fields when present.
    /// </summary>
    private static Guid? TryGetTargetColumnId(ProposalOperationDto op)
    {
        // Target columns for column-targeted card movement/creation operations.
        if (op.TargetType.Equals("column", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(op.TargetId)
            && Guid.TryParse(op.TargetId, out var colTargetId))
        {
            return colTargetId;
        }

        if (string.IsNullOrWhiteSpace(op.Parameters)) return null;

        // Parse parameters JSON for columnId / targetColumnId fields
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(op.Parameters);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                return null;

            if (doc.RootElement.TryGetProperty("columnId", out var colProp)
                && colProp.ValueKind == System.Text.Json.JsonValueKind.String
                && Guid.TryParse(colProp.GetString(), out var columnId))
            {
                return columnId;
            }

            if (doc.RootElement.TryGetProperty("targetColumnId", out var targetColProp)
                && targetColProp.ValueKind == System.Text.Json.JsonValueKind.String
                && Guid.TryParse(targetColProp.GetString(), out var targetColumnId))
            {
                return targetColumnId;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Malformed JSON in parameters -- skip silently
        }

        return null;
    }

    private static Guid? TryGetCardId(ProposalOperationDto operation)
    {
        if (!operation.ActionType.Equals("create", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(operation.Parameters))
        {
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(operation.Parameters);
                if (document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                    document.RootElement.TryGetProperty("cardId", out var cardId) &&
                    cardId.ValueKind == System.Text.Json.JsonValueKind.String &&
                    Guid.TryParse(cardId.GetString(), out var parameterCardId))
                    return parameterCardId;
            }
            catch (System.Text.Json.JsonException)
            {
                // Malformed operations are rejected by the operation contract, not this warning view.
            }
        }
        return Guid.TryParse(operation.TargetId, out var targetCardId) ? targetCardId : null;
    }

    private readonly record struct ColumnProjection(int Delta, bool ReceivesCards, int PeakIncomingDelta);

    private static IReadOnlyList<string> GetWebhookEventTypes(ProposalConflictContext proposal)
    {
        return proposal.Operations
            .Select(ToWebhookEventType)
            .Where(eventType => eventType is not null)
            .Distinct(StringComparer.Ordinal)
            .Select(eventType => eventType!)
            .ToList();
    }

    private static string? ToWebhookEventType(ProposalOperationDto operation)
    {
        if (string.IsNullOrWhiteSpace(operation.TargetType) || string.IsNullOrWhiteSpace(operation.ActionType))
            return null;

        var entityType = operation.TargetType.Trim().ToLowerInvariant();
        var eventOperation = operation.ActionType.Trim().ToLowerInvariant() switch
        {
            "create" or "add" => "created",
            "move" => "moved",
            "delete" or "remove" => "deleted",
            "archive" or "update" or "set" or "rename" or "reorder" or "assign" or "attach" or "block" or "unblock" or "restore" or "unarchive" => "updated",
            _ => null
        };

        return eventOperation is null ? null : $"{entityType}.{eventOperation}";
    }

    private static ProposalConflictContext ToContext(AutomationProposal proposal) => new(
        proposal.Id,
        proposal.BoardId,
        proposal.RequestedByUserId,
        proposal.RiskLevel,
        proposal.CreatedAt,
        proposal.Operations.Select(operation => new ProposalOperationDto(
            operation.Id,
            operation.ProposalId,
            operation.Sequence,
            operation.ActionType,
            operation.TargetType,
            operation.TargetId,
            operation.Parameters,
            operation.IdempotencyKey,
            operation.ExpectedVersion)).ToList());

    private static ProposalConflictContext ToContext(ProposalDto proposal) => new(
        proposal.Id,
        proposal.BoardId,
        proposal.RequestedByUserId,
        proposal.RiskLevel,
        proposal.CreatedAt,
        proposal.Operations);

    private sealed record ProposalConflictContext(
        Guid Id,
        Guid? BoardId,
        Guid RequestedByUserId,
        RiskLevel RiskLevel,
        DateTimeOffset CreatedAt,
        IReadOnlyList<ProposalOperationDto> Operations);
}
