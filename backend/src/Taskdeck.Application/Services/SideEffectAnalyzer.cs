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
    // Action types that actively mutate cards
    private static readonly HashSet<string> CardMutatingActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "create", "move", "archive", "update", "delete", "bulk_move"
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

    internal static IReadOnlyList<SideEffectRow> BuildSideEffectRows(
        IReadOnlyList<AutomationProposalOperation> operations,
        bool hasActiveWebhooks)
    {
        bool hasCardMutation = operations.Any(op =>
            CardMutatingActions.Contains(op.ActionType) &&
            string.Equals(op.TargetType, "card", StringComparison.OrdinalIgnoreCase));
        bool hasColumnMutation = operations.Any(op =>
            string.Equals(op.TargetType, "column", StringComparison.OrdinalIgnoreCase));
        bool hasBoardMutation = hasCardMutation || hasColumnMutation;
        bool hasAnyOperation = operations.Count > 0;

        return new List<SideEffectRow>
        {
            new(
                "Cards",
                hasBoardMutation
                    ? hasCardMutation && hasColumnMutation
                        ? "Creates, moves, or archives cards and adds columns on the board"
                        : hasCardMutation
                            ? "Creates, moves, or archives cards on the board"
                            : "Adds columns to the board (no direct card mutations)"
                    : "No board mutations",
                hasBoardMutation ? SideEffectTone.Active : SideEffectTone.Passive),

            new(
                "Subtasks",
                "Subtask management not yet supported",
                SideEffectTone.Passive),

            new(
                "Comments",
                "Proposals do not create comments",
                SideEffectTone.Passive),

            new(
                "Activity log",
                hasAnyOperation
                    ? "Audit entries will be recorded for all applied operations"
                    : "No operations to log",
                hasAnyOperation ? SideEffectTone.Active : SideEffectTone.Passive),

            new(
                "Notifications",
                hasAnyOperation
                    ? "Approval or rejection generates notifications"
                    : "No notifications generated",
                hasAnyOperation ? SideEffectTone.Active : SideEffectTone.Passive),

            new(
                "Webhooks",
                hasActiveWebhooks && hasAnyOperation
                    ? "Outbound webhooks configured for this board will fire"
                    : hasActiveWebhooks
                        ? "Outbound webhooks configured but no operations to trigger them"
                        : "No outbound webhooks configured",
                hasActiveWebhooks && hasAnyOperation ? SideEffectTone.Active : SideEffectTone.Passive),

            new(
                "Calendar",
                "Calendar integration not yet available",
                SideEffectTone.Passive)
        };
    }

    internal static Reversibility ComputeApplyRiskPosture(
        IReadOnlyList<AutomationProposalOperation> operations,
        RiskLevel riskLevel)
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

        if (operations.Count == 0)
        {
            summary = "No operations to apply";
            description = "This proposal contains no operations and will have no effect.";
            windowMs = Reversibility.DefaultWindowMs;
        }

        return new Reversibility(summary, description, windowMs);
    }
}
