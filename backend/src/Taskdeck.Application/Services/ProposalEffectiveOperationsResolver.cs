using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Services;

/// <summary>
/// Single source of truth for selecting and materializing the operation set a
/// proposal read must describe. Approval/Apply and related review evidence must
/// never choose revisions by different rules.
/// </summary>
internal static class ProposalEffectiveOperationsResolver
{
    internal static bool CanHaveEffectiveRevision(AutomationProposal proposal) =>
        proposal.ApprovedRevisionId is not null
        || proposal.Status is ProposalStatus.PendingReview or ProposalStatus.Rejected;

    internal static ProposalRevisionRef? SelectEffectiveRevisionRef(
        AutomationProposal proposal,
        IReadOnlyList<ProposalRevisionRef> refsForProposal)
    {
        if (proposal.ApprovedRevisionId is Guid approvedRevisionId)
            return refsForProposal.FirstOrDefault(reference => reference.Id == approvedRevisionId);

        if (proposal.Status is ProposalStatus.PendingReview)
            return refsForProposal.MaxBy(reference => reference.RevisionNumber);

        if (proposal.Status is ProposalStatus.Rejected)
        {
            if (proposal.DecidedAt is not DateTime decidedAt)
                return refsForProposal.MaxBy(reference => reference.RevisionNumber);

            return refsForProposal
                .Where(reference => reference.RevisedAt.UtcDateTime <= decidedAt)
                .OrderByDescending(reference => reference.RevisedAt)
                .ThenByDescending(reference => reference.RevisionNumber)
                .FirstOrDefault();
        }

        return null;
    }

    /// <summary>
    /// Resolves a bounded page in two phases: payload-free refs select at most
    /// one winner per proposal, then only those winner payloads are loaded. Every
    /// supplied proposal appears in the result; missing or malformed winners
    /// degrade to the persisted original operations, matching proposal DTO reads.
    /// </summary>
    internal static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ProposalOperationDto>>> ResolveAsync(
        IProposalRevisionRepository revisions,
        IReadOnlyCollection<AutomationProposal> proposals,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(revisions);
        ArgumentNullException.ThrowIfNull(proposals);

        var resolved = proposals.ToDictionary(
            proposal => proposal.Id,
            proposal => (IReadOnlyList<ProposalOperationDto>)OriginalOperations(proposal));
        var candidates = proposals.Where(CanHaveEffectiveRevision).ToList();
        if (candidates.Count == 0)
            return resolved;

        var refs = await revisions.GetRefsByProposalIdsAsync(
            candidates.Select(proposal => proposal.Id),
            cancellationToken);
        var refsByProposal = refs
            .GroupBy(reference => reference.ProposalId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ProposalRevisionRef>)group.ToList());

        var winners = new Dictionary<Guid, Guid>();
        foreach (var proposal in candidates)
        {
            if (!refsByProposal.TryGetValue(proposal.Id, out var proposalRefs))
                continue;

            if (SelectEffectiveRevisionRef(proposal, proposalRefs) is { } winner)
                winners[proposal.Id] = winner.Id;
        }

        if (winners.Count == 0)
            return resolved;

        var winningRevisions = await revisions.GetByIdsAsync(winners.Values, cancellationToken);
        var byRevisionId = winningRevisions.ToDictionary(revision => revision.Id);

        foreach (var proposal in candidates)
        {
            if (!winners.TryGetValue(proposal.Id, out var revisionId)
                || !byRevisionId.TryGetValue(revisionId, out var revision)
                || !ProposalRevisionPayload.TryParseOperations(
                    proposal.Id,
                    revision.RevisedPayload,
                    out var revisedOperations,
                    out _))
            {
                continue;
            }

            resolved[proposal.Id] = revisedOperations
                .OrderBy(operation => operation.Sequence)
                .ToList();
        }

        return resolved;
    }

    private static List<ProposalOperationDto> OriginalOperations(AutomationProposal proposal) =>
        proposal.Operations
            .OrderBy(operation => operation.Sequence)
            .Select(operation => new ProposalOperationDto(
                operation.Id,
                operation.ProposalId,
                operation.Sequence,
                operation.ActionType,
                operation.TargetType,
                operation.TargetId,
                operation.Parameters,
                operation.IdempotencyKey,
                operation.ExpectedVersion))
            .ToList();
}
