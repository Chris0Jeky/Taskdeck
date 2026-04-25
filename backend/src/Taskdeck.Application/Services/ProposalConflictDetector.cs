using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
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

    public ProposalConflictDetector(
        IUnitOfWork unitOfWork,
        IAuthorizationService authorizationService)
    {
        _unitOfWork = unitOfWork;
        _authorizationService = authorizationService;
    }

    public async Task<Result<IReadOnlyList<ConflictRowDto>>> DetectConflictsAsync(
        Guid proposalId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var proposal = await _unitOfWork.AutomationProposals.GetByIdAsync(proposalId, cancellationToken);
        if (proposal is null)
            return Result.Failure<IReadOnlyList<ConflictRowDto>>(ErrorCodes.NotFound, "Proposal not found");

        // Authorization: user must own the proposal or have board read access
        var authResult = await AuthorizeAccessAsync(proposal, userId, cancellationToken);
        if (!authResult.IsSuccess)
            return Result.Failure<IReadOnlyList<ConflictRowDto>>(authResult.ErrorCode, authResult.ErrorMessage);

        var rows = new List<ConflictRow>();
        var flaggedCardIds = new HashSet<Guid>();
        var flaggedColumnIds = new HashSet<Guid>();

        // Check each condition and collect rows
        await CheckStaleDataAsync(proposal, rows, flaggedCardIds, cancellationToken);
        await CheckWipLimitAsync(proposal, rows, flaggedColumnIds, cancellationToken);
        await CheckDuplicatePendingProposalsAsync(proposal, rows, cancellationToken);
        CheckHighRiskOperations(proposal, rows);
        await CheckOutboundWebhooksAsync(proposal, rows, cancellationToken);
        await CheckActiveCommentsAsync(proposal, rows, cancellationToken);
        CheckMultipleOperationsOnSameCard(proposal, rows);

        // If no warnings or info rows, emit an Ok row
        if (rows.Count == 0)
        {
            rows.Add(new ConflictRow(ConflictTone.Ok, "status", "No conflicts detected"));
        }
        else
        {
            // Add positive signals when applicable
            await AddPositiveSignalsAsync(proposal, rows, flaggedCardIds, flaggedColumnIds, cancellationToken);
        }

        // Sort: Warn first, then Info, then Ok
        var sorted = rows
            .OrderBy(r => r.Tone)
            .ToList();

        return Result.Success<IReadOnlyList<ConflictRowDto>>(
            sorted.Select(ConflictRowDto.FromDomain).ToList());
    }

    private async Task<Result> AuthorizeAccessAsync(
        AutomationProposal proposal,
        Guid userId,
        CancellationToken cancellationToken)
    {
        // Owner always has access
        if (proposal.RequestedByUserId == userId)
            return Result.Success();

        // If board-scoped, check board read access
        if (proposal.BoardId.HasValue)
        {
            var canRead = await _authorizationService.CanReadBoardAsync(userId, proposal.BoardId.Value);
            if (canRead.IsSuccess && canRead.Value)
                return Result.Success();
        }

        return Result.Failure(ErrorCodes.Forbidden, "You do not have permission to view conflicts for this proposal");
    }

    /// <summary>
    /// Warn: target card was modified since the proposal was generated.
    /// Compares card's UpdatedAt against proposal's CreatedAt.
    /// </summary>
    private async Task CheckStaleDataAsync(
        AutomationProposal proposal,
        List<ConflictRow> rows,
        HashSet<Guid> flaggedCardIds,
        CancellationToken cancellationToken)
    {
        var cardTargetIds = GetDistinctCardTargetIds(proposal);
        if (cardTargetIds.Count == 0) return;

        foreach (var cardId in cardTargetIds)
        {
            var card = await _unitOfWork.Cards.GetByIdAsync(cardId, cancellationToken);
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
    /// Checks operations that move or create cards into a column.
    /// </summary>
    private async Task CheckWipLimitAsync(
        AutomationProposal proposal,
        List<ConflictRow> rows,
        HashSet<Guid> flaggedColumnIds,
        CancellationToken cancellationToken)
    {
        var targetColumnIds = GetTargetColumnIds(proposal);
        if (targetColumnIds.Count == 0) return;

        foreach (var columnId in targetColumnIds)
        {
            var column = await _unitOfWork.Columns.GetByIdWithCardsAsync(columnId, cancellationToken);
            if (column is null) continue;

            if (column.WipLimit.HasValue && column.Cards.Count >= column.WipLimit.Value)
            {
                flaggedColumnIds.Add(columnId);
                rows.Add(new ConflictRow(
                    ConflictTone.Warn,
                    "wip-limit",
                    $"Column \"{column.Name}\" is at WIP limit ({column.Cards.Count}/{column.WipLimit.Value})"));
            }
        }
    }

    /// <summary>
    /// Warn: another pending proposal targets the same card.
    /// </summary>
    private async Task CheckDuplicatePendingProposalsAsync(
        AutomationProposal proposal,
        List<ConflictRow> rows,
        CancellationToken cancellationToken)
    {
        var cardTargetIds = GetDistinctCardTargetIds(proposal);
        if (cardTargetIds.Count == 0) return;

        foreach (var cardId in cardTargetIds)
        {
            var existing = await _unitOfWork.AutomationProposals
                .GetLatestByOperationTargetAsync("card", cardId.ToString(), cancellationToken);

            if (existing is not null
                && existing.Id != proposal.Id
                && existing.Status == ProposalStatus.PendingReview)
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
        AutomationProposal proposal,
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
        AutomationProposal proposal,
        List<ConflictRow> rows,
        CancellationToken cancellationToken)
    {
        if (!proposal.BoardId.HasValue) return;

        var webhooks = await _unitOfWork.OutboundWebhookSubscriptions
            .GetActiveByBoardAsync(proposal.BoardId.Value, cancellationToken);

        if (webhooks.Count > 0)
        {
            rows.Add(new ConflictRow(
                ConflictTone.Info,
                "webhooks",
                $"This proposal will trigger {webhooks.Count} outbound webhook(s)"));
        }
    }

    /// <summary>
    /// Info: target card has active comments/discussion.
    /// </summary>
    private async Task CheckActiveCommentsAsync(
        AutomationProposal proposal,
        List<ConflictRow> rows,
        CancellationToken cancellationToken)
    {
        var cardTargetIds = GetDistinctCardTargetIds(proposal);
        if (cardTargetIds.Count == 0) return;

        foreach (var cardId in cardTargetIds)
        {
            var comments = await _unitOfWork.CardComments.GetByCardIdAsync(cardId, cancellationToken);
            var commentCount = comments.Count();
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
        AutomationProposal proposal,
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
    /// </summary>
    private async Task AddPositiveSignalsAsync(
        AutomationProposal proposal,
        List<ConflictRow> rows,
        HashSet<Guid> flaggedCardIds,
        HashSet<Guid> flaggedColumnIds,
        CancellationToken cancellationToken)
    {
        // Ok: target column has capacity (only if we didn't already warn about WIP for this column)
        var targetColumnIds = GetTargetColumnIds(proposal);
        foreach (var columnId in targetColumnIds)
        {
            if (flaggedColumnIds.Contains(columnId)) continue;

            var column = await _unitOfWork.Columns.GetByIdWithCardsAsync(columnId, cancellationToken);
            if (column is null) continue;

            if (column.WipLimit.HasValue)
            {
                rows.Add(new ConflictRow(
                    ConflictTone.Ok,
                    "capacity",
                    $"Column \"{column.Name}\" has capacity ({column.Cards.Count}/{column.WipLimit.Value})"));
            }
        }

        // Ok: card data is fresh (only for cards we didn't already flag as stale/missing)
        var cardTargetIds = GetDistinctCardTargetIds(proposal);
        foreach (var cardId in cardTargetIds)
        {
            if (flaggedCardIds.Contains(cardId)) continue;

            var card = await _unitOfWork.Cards.GetByIdAsync(cardId, cancellationToken);
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
    /// </summary>
    private static List<Guid> GetDistinctCardTargetIds(AutomationProposal proposal)
    {
        return proposal.Operations
            .Where(op => op.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase)
                         && !string.IsNullOrEmpty(op.TargetId)
                         && Guid.TryParse(op.TargetId, out _))
            .Select(op => Guid.Parse(op.TargetId!))
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Extracts target column IDs from operations that move or create into a column.
    /// Parses the JSON parameters for "columnId" or "targetColumnId" fields.
    /// </summary>
    private static List<Guid> GetTargetColumnIds(AutomationProposal proposal)
    {
        var columnIds = new HashSet<Guid>();

        foreach (var op in proposal.Operations)
        {
            if (string.IsNullOrWhiteSpace(op.Parameters)) continue;

            // Target columns for column-targeted operations
            if (op.TargetType.Equals("column", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(op.TargetId)
                && Guid.TryParse(op.TargetId, out var colTargetId))
            {
                columnIds.Add(colTargetId);
                continue;
            }

            // Parse parameters JSON for columnId / targetColumnId fields
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(op.Parameters);
                if (doc.RootElement.TryGetProperty("columnId", out var colProp)
                    && Guid.TryParse(colProp.GetString(), out var columnId))
                {
                    columnIds.Add(columnId);
                }
                else if (doc.RootElement.TryGetProperty("targetColumnId", out var targetColProp)
                         && Guid.TryParse(targetColProp.GetString(), out var targetColumnId))
                {
                    columnIds.Add(targetColumnId);
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // Malformed JSON in parameters -- skip silently
            }
        }

        return columnIds.ToList();
    }
}
