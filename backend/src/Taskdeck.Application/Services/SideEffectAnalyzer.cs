using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

/// <summary>
/// Analyzes a proposal's operations to produce a 7-category side-effect breakdown
/// (Cards, Subtasks, Comments, Activity log, Notifications, Webhooks, Calendar)
/// and an apply-risk posture.
/// </summary>
public sealed class SideEffectAnalyzer : ISideEffectAnalyzer
{
    // Keep a stable display order and name the actual effect: legacy "archive" blocks a card;
    // only the lifecycle actions change its archived state. Move aliases share one disclosure.
    private static readonly (string Action, string Verb)[] CardMutationVerbs =
    {
        ("create", "creates"),
        ("move", "moves"),
        ("bulk_move", "moves"),
        ("archive", "blocks"),
        ("update", "updates"),
        ("delete", "deletes"),
        ("archive-lifecycle", "archives"),
        ("restore-lifecycle", "restores")
    };

    private readonly IUnitOfWork _unitOfWork;

    public SideEffectAnalyzer(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    public async Task<Result<ProposalSideEffectsDto>> AnalyzeAsync(
        Guid proposalId,
        CancellationToken cancellationToken = default)
    {
        var proposal = await _unitOfWork.AutomationProposals.GetByIdAsync(proposalId, cancellationToken);
        if (proposal is null)
            return Result.Failure<ProposalSideEffectsDto>(ErrorCodes.NotFound, "Proposal not found.");

        var operations = proposal.Operations;

        // Determine webhook status for the board
        bool hasActiveWebhooks = false;
        if (proposal.BoardId.HasValue)
        {
            var webhookSubs = await _unitOfWork.OutboundWebhookSubscriptions
                .GetActiveByBoardAsync(proposal.BoardId.Value, cancellationToken);
            hasActiveWebhooks = webhookSubs.Count > 0;
        }

        var rows = BuildSideEffectRows(operations, hasActiveWebhooks);
        var applyRisk = ComputeApplyRiskPosture(operations, proposal.RiskLevel);

        var dto = new ProposalSideEffectsDto(
            Rows: rows.Select(r => new SideEffectRowDto(r.Key, r.Value, r.Tone.ToString().ToLowerInvariant())).ToList(),
            Reversibility: new ReversibilityDto(applyRisk.Summary, applyRisk.Description, applyRisk.WindowMs));

        return Result.Success(dto);
    }

    public async Task<Result<ProposalSideEffectsDto>> AnalyzeAsync(
        ProposalDto effectiveProposal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(effectiveProposal);

        var hasActiveWebhooks = false;
        if (effectiveProposal.BoardId.HasValue)
        {
            var webhookSubs = await _unitOfWork.OutboundWebhookSubscriptions
                .GetActiveByBoardAsync(effectiveProposal.BoardId.Value, cancellationToken);
            hasActiveWebhooks = webhookSubs.Count > 0;
        }

        var rows = BuildSideEffectRows(effectiveProposal.Operations, hasActiveWebhooks);
        var applyRisk = ComputeApplyRiskPosture(effectiveProposal.Operations, effectiveProposal.RiskLevel);

        return Result.Success(new ProposalSideEffectsDto(
            Rows: rows.Select(r => new SideEffectRowDto(r.Key, r.Value, r.Tone.ToString().ToLowerInvariant())).ToList(),
            Reversibility: new ReversibilityDto(applyRisk.Summary, applyRisk.Description, applyRisk.WindowMs)));
    }

    internal static IReadOnlyList<SideEffectRow> BuildSideEffectRows(
        IReadOnlyList<AutomationProposalOperation> operations,
        bool hasActiveWebhooks)
    {
        return BuildSideEffectRows(operations.Select(ToDto).ToList(), hasActiveWebhooks);
    }

    internal static IReadOnlyList<SideEffectRow> BuildSideEffectRows(
        IReadOnlyList<ProposalOperationDto> operations,
        bool hasActiveWebhooks)
    {
        var cardActions = operations
            .Where(op => string.Equals(op.TargetType, "card", StringComparison.OrdinalIgnoreCase))
            .Select(op => op.ActionType)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cardVerbs = CardMutationVerbs
            .Where(entry => cardActions.Contains(entry.Action))
            .Select(entry => entry.Verb)
            .Distinct()
            .ToList();
        var hasCardMutation = cardVerbs.Count > 0;
        var cardMutationSummary = DescribeCardMutations(cardVerbs);
        var hasColumnMutation = operations.Any(op =>
            string.Equals(op.TargetType, "column", StringComparison.OrdinalIgnoreCase));
        var hasBoardMutation = hasCardMutation || hasColumnMutation;
        var hasAnyOperation = operations.Count > 0;

        return new List<SideEffectRow>
        {
            new(
                "Cards",
                hasBoardMutation
                    ? hasCardMutation && hasColumnMutation
                        ? $"{cardMutationSummary} and adds columns on the board"
                        : hasCardMutation
                            ? $"{cardMutationSummary} on the board"
                            : "Adds columns to the board (no direct card mutations)"
                    : "No board mutations",
                hasBoardMutation ? SideEffectTone.Active : SideEffectTone.Passive),
            new("Subtasks", "Subtask management not yet supported", SideEffectTone.Passive),
            new("Comments", "Proposals do not create comments", SideEffectTone.Passive),
            new(
                "Activity log",
                hasAnyOperation ? "Audit entries will be recorded for all applied operations" : "No operations to log",
                hasAnyOperation ? SideEffectTone.Active : SideEffectTone.Passive),
            new(
                "Notifications",
                hasAnyOperation ? "Approval or rejection generates notifications" : "No notifications generated",
                hasAnyOperation ? SideEffectTone.Active : SideEffectTone.Passive),
            new(
                "Webhooks",
                hasActiveWebhooks && hasAnyOperation
                    ? "Outbound webhooks configured for this board will fire"
                    : hasActiveWebhooks
                        ? "Outbound webhooks configured but no operations to trigger them"
                        : "No outbound webhooks configured",
                hasActiveWebhooks && hasAnyOperation ? SideEffectTone.Active : SideEffectTone.Passive),
            new("Calendar", "Calendar integration not yet available", SideEffectTone.Passive)
        };
    }

    private static string DescribeCardMutations(IReadOnlyList<string> verbs)
    {
        if (verbs.Count == 0)
            return string.Empty;

        var actions = verbs.Count switch
        {
            1 => verbs[0],
            2 => string.Join(" and ", verbs),
            _ => $"{string.Join(", ", verbs.Take(verbs.Count - 1))}, and {verbs[^1]}"
        };

        return $"{char.ToUpperInvariant(actions[0])}{actions[1..]} cards";
    }

    internal static Reversibility ComputeApplyRiskPosture(
        IReadOnlyList<AutomationProposalOperation> operations,
        RiskLevel riskLevel)
    {
        return ComputeApplyRiskPostureForCount(operations.Count, riskLevel);
    }

    internal static Reversibility ComputeApplyRiskPosture(
        IReadOnlyList<ProposalOperationDto> operations,
        RiskLevel riskLevel)
    {
        return ComputeApplyRiskPostureForCount(operations.Count, riskLevel);
    }

    private static Reversibility ComputeApplyRiskPostureForCount(int operationCount, RiskLevel riskLevel)
    {
        // WindowMs is retained for the stable side-effect endpoint contract. It is a legacy
        // review-attention horizon, not an undo or recovery guarantee.
        long windowMs = Reversibility.DefaultWindowMs;

        string summary;
        string description;

        switch (riskLevel)
        {
            case RiskLevel.Critical:
                windowMs = Reversibility.DefaultWindowMs / 2;
                summary = "Critical risk · manual recovery";
                description = "Critical-risk operations may remove data or trigger downstream effects. " +
                              "Inspect every operation before applying; recovery may require manual intervention.";
                break;

            case RiskLevel.High:
                summary = "High risk · inspect every change";
                description = "High-risk operations can affect multiple records or external systems. " +
                              "Review targets and downstream effects before applying.";
                break;

            case RiskLevel.Medium:
                summary = "Medium risk · review affected items";
                description = "Medium-risk operations change board state. " +
                              "Review the affected items before applying.";
                break;

            case RiskLevel.Low:
            default:
                summary = "Low risk · confirm before apply";
                description = "Low-risk operations still change board state. " +
                              "Confirm the affected items before applying.";
                break;
        }

        if (operationCount == 0)
        {
            summary = "No operations to apply";
            description = "This proposal contains no operations and will have no effect.";
            windowMs = Reversibility.DefaultWindowMs;
        }

        return new Reversibility(summary, description, windowMs);
    }

    private static ProposalOperationDto ToDto(AutomationProposalOperation operation) => new(
        operation.Id,
        operation.ProposalId,
        operation.Sequence,
        operation.ActionType,
        operation.TargetType,
        operation.TargetId,
        operation.Parameters,
        operation.IdempotencyKey,
        operation.ExpectedVersion);
}
