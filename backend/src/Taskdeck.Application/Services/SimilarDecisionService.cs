using System.Globalization;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Taskdeck.Domain.SimilarPast;

namespace Taskdeck.Application.Services;

/// <summary>
/// Queries past proposals with the same action class to surface similar
/// historical decisions and an aggregate apply rate.
/// </summary>
public class SimilarDecisionService : ISimilarDecisionService
{
    /// <summary>
    /// Maximum number of similar decisions to return.
    /// </summary>
    internal const int MaxDecisions = 3;

    /// <summary>
    /// Maximum number of past proposals to query for rate calculation.
    /// Limits the lookback window to avoid unbounded queries.
    /// </summary>
    internal const int LookbackLimit = 200;

    private readonly IUnitOfWork _unitOfWork;

    public SimilarDecisionService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    public async Task<Result<SimilarPastResultDto>> GetSimilarPastAsync(
        Guid proposalId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // Load the current proposal to determine its action class
        var proposal = await _unitOfWork.AutomationProposals.GetByIdAsync(proposalId, cancellationToken);
        if (proposal is null)
            return Result.Failure<SimilarPastResultDto>(ErrorCodes.NotFound, "Proposal not found.");

        // Determine the primary action type from the proposal's operations
        var actionType = GetPrimaryActionType(proposal);
        if (string.IsNullOrWhiteSpace(actionType))
        {
            // Proposal has no operations -- return empty result
            return Result.Success(ToDto(SimilarPastResult.Empty));
        }

        // Query past proposals with the same action class in terminal states
        // First try board-scoped, then fall back to user-scoped
        var pastProposals = (await _unitOfWork.AutomationProposals
            .GetTerminalByActionTypeAsync(actionType, proposal.BoardId, userId, LookbackLimit, cancellationToken))
            ?? Array.Empty<AutomationProposal>();

        if (pastProposals.Count == 0 && proposal.BoardId.HasValue)
        {
            // Board doesn't have enough history -- fall back to user-scoped query
            pastProposals = (await _unitOfWork.AutomationProposals
                .GetTerminalByActionTypeAsync(actionType, boardId: null, userId, LookbackLimit, cancellationToken))
                ?? Array.Empty<AutomationProposal>();
        }

        // Exclude the current proposal itself from the results
        var filtered = pastProposals
            .Where(p => p.Id != proposalId)
            .ToList();

        if (filtered.Count == 0)
            return Result.Success(ToDto(SimilarPastResult.Empty));

        // Compute apply rate across ALL matching proposals (not just top 3)
        var appliedCount = filtered.Count(p => p.Status == ProposalStatus.Applied);
        var rejectedCount = filtered.Count(p => p.Status == ProposalStatus.Rejected);
        var applyRate = SimilarPastResult.ComputeApplyRate(appliedCount, rejectedCount);

        // Take the 3 most recent for display
        var topDecisions = filtered
            .Take(MaxDecisions)
            .Select((p, index) => SimilarPastDecision.Create(
                serial: $"#{(index + 1):D3}",
                title: GetProposalTitle(p),
                verdict: MapVerdict(p.Status),
                date: FormatWeekDate(p.DecidedAt ?? p.CreatedAt)))
            .ToList();

        var result = new SimilarPastResult(topDecisions, applyRate);
        return Result.Success(ToDto(result));
    }

    /// <summary>
    /// Gets the primary action type from the first operation of a proposal.
    /// The "action class" is the ActionType string of the first (or most representative) operation.
    /// </summary>
    internal static string? GetPrimaryActionType(AutomationProposal proposal)
    {
        if (proposal.Operations.Count == 0)
            return null;

        // Use the first operation's action type as the primary action class.
        // For multi-operation proposals (e.g. bulk_move generates multiple "move" ops),
        // the first operation is representative.
        return proposal.Operations
            .OrderBy(op => op.Sequence)
            .First()
            .ActionType;
    }

    /// <summary>
    /// Gets a display title from a proposal: its summary, or the first operation description.
    /// </summary>
    internal static string GetProposalTitle(AutomationProposal proposal)
    {
        if (!string.IsNullOrWhiteSpace(proposal.Summary))
            return proposal.Summary;

        if (proposal.Operations.Count > 0)
        {
            var firstOp = proposal.Operations
                .OrderBy(op => op.Sequence)
                .First();
            return $"{firstOp.ActionType} {firstOp.TargetType}";
        }

        return "Untitled proposal";
    }

    /// <summary>
    /// Maps a proposal terminal status to a <see cref="PastVerdict"/>.
    /// </summary>
    internal static PastVerdict MapVerdict(ProposalStatus status)
    {
        return status switch
        {
            ProposalStatus.Applied => PastVerdict.Applied,
            ProposalStatus.Rejected => PastVerdict.Rejected,
            _ => throw new InvalidOperationException(
                $"Cannot map non-terminal status '{status}' to a PastVerdict.")
        };
    }

    /// <summary>
    /// Formats a DateTime as an ISO week string, e.g. 'wk 14'.
    /// Uses ISO 8601 week numbering (Monday-start, first 4-day week).
    /// </summary>
    internal static string FormatWeekDate(DateTimeOffset dateTime)
    {
        var weekNumber = ISOWeek.GetWeekOfYear(dateTime.DateTime);
        return $"wk {weekNumber}";
    }

    private static SimilarPastResultDto ToDto(SimilarPastResult result)
    {
        var decisions = result.Decisions
            .Select(d => new SimilarPastDecisionDto(
                d.Serial,
                d.Title,
                d.Verdict.ToString().ToLowerInvariant(),
                d.Date))
            .ToList();

        return new SimilarPastResultDto(decisions, result.ApplyRate);
    }
}
