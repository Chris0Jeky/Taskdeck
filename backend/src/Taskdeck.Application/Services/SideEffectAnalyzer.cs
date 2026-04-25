using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

/// <summary>
/// Analyzes a proposal's operations to produce a 7-category side-effect breakdown
/// (Cards, Subtasks, Comments, Activity log, Notifications, Webhooks, Calendar)
/// and a reversibility posture.
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
        Guid userId,
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
        var reversibility = ComputeReversibility(operations, proposal.RiskLevel);

        var dto = new ProposalSideEffectsDto(
            Rows: rows.Select(r => new SideEffectRowDto(r.Key, r.Value, r.Tone.ToString().ToLowerInvariant())).ToList(),
            Reversibility: new ReversibilityDto(reversibility.Summary, reversibility.Description, reversibility.WindowMs));

        return Result.Success(dto);
    }

    internal static IReadOnlyList<SideEffectRow> BuildSideEffectRows(
        IReadOnlyList<AutomationProposalOperation> operations,
        bool hasActiveWebhooks)
    {
        bool hasCardMutation = operations.Any(op => CardMutatingActions.Contains(op.ActionType));
        bool hasColumnMutation = operations.Any(op =>
            string.Equals(op.TargetType, "column", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(op.ActionType, "create_column", StringComparison.OrdinalIgnoreCase));
        bool hasAnyOperation = operations.Count > 0;

        return new List<SideEffectRow>
        {
            new(
                "Cards",
                hasCardMutation
                    ? "Creates, moves, or archives cards on the board"
                    : "No card mutations",
                hasCardMutation ? SideEffectTone.Active : SideEffectTone.Passive),

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
                hasActiveWebhooks
                    ? "Outbound webhooks configured for this board will fire"
                    : "No outbound webhooks configured",
                hasActiveWebhooks ? SideEffectTone.Active : SideEffectTone.Passive),

            new(
                "Calendar",
                "Calendar integration not yet available",
                SideEffectTone.Passive)
        };
    }

    internal static Reversibility ComputeReversibility(
        IReadOnlyList<AutomationProposalOperation> operations,
        RiskLevel riskLevel)
    {
        // Base window is 6 hours
        long windowMs = Reversibility.DefaultWindowMs;

        // Adjust window based on risk level
        string summary;
        string description;

        switch (riskLevel)
        {
            case RiskLevel.Critical:
                windowMs = Reversibility.DefaultWindowMs / 2; // 3 hours -- tighter for critical
                summary = "3 hours · manual intervention required";
                description = "Critical-risk operations may require manual intervention to reverse. " +
                              "Archive and delete operations can be recovered within the window, " +
                              "but downstream effects (webhooks, notifications) cannot be recalled.";
                break;

            case RiskLevel.High:
                windowMs = Reversibility.DefaultWindowMs; // 6 hours
                summary = "6 hours · single keystroke";
                description = "High-risk operations can be reversed within the window. " +
                              "Card moves and updates are fully reversible; " +
                              "archived cards can be restored from the archive.";
                break;

            case RiskLevel.Medium:
                windowMs = Reversibility.DefaultWindowMs; // 6 hours
                summary = "6 hours · single keystroke";
                description = "Medium-risk operations are fully reversible within the window. " +
                              "All board mutations can be undone from the activity log.";
                break;

            case RiskLevel.Low:
            default:
                windowMs = Reversibility.DefaultWindowMs; // 6 hours
                summary = "6 hours · single keystroke";
                description = "Low-risk operations are fully reversible within the window. " +
                              "All board mutations can be undone from the activity log.";
                break;
        }

        // If there are no operations, the proposal is a no-op
        if (operations.Count == 0)
        {
            summary = "6 hours · no operations";
            description = "This proposal contains no operations and will have no effect.";
            windowMs = Reversibility.DefaultWindowMs;
        }

        return new Reversibility(summary, description, windowMs);
    }
}
