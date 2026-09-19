using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

/// <summary>
/// Single source of truth for selecting and materializing the operation set a
/// proposal read must describe. Approval/Apply and related review evidence must
/// never choose revisions by different rules.
/// </summary>
internal static class ProposalEffectiveOperationsResolver
{
    /// <summary>
    /// True when <paramref name="proposal"/> could resolve to an effective revision at all, letting
    /// read paths skip the revision query entirely for proposals that always use their original
    /// operations (any status other than PendingReview/Rejected without a pin).
    /// </summary>
    internal static bool CanHaveEffectiveRevision(AutomationProposal proposal) =>
        proposal.ApprovedRevisionId is not null
        || proposal.Status is ProposalStatus.PendingReview or ProposalStatus.Rejected;

    /// <summary>
    /// The single implementation of the effective-revision rules, applied to the revision METADATA
    /// of ONE proposal. <paramref name="refsForProposal"/> must contain only refs of
    /// <paramref name="proposal"/>; ordering within it is irrelevant (selection is explicit).
    /// </summary>
    internal static ProposalRevisionRef? SelectEffectiveRevisionRef(
        AutomationProposal proposal,
        IReadOnlyList<ProposalRevisionRef> refsForProposal)
    {
        // A pin is resolved within the proposal's OWN revisions. A pin is only ever set from a
        // revision of the same proposal (ApproveProposalAsync pins what GetLatestByProposalIdAsync
        // returned), so this agrees with a global by-id lookup for every reachable state while
        // making a cross-proposal id structurally unable to render as this proposal's content.
        //
        // Two asymmetries against AutomationExecutorService.MaterializeEffectiveProposalAsync,
        // stated in full because the containment above is only half the story (#1444 review):
        //  - Scope: the executor resolves the pin GLOBALLY by id and does not check ProposalId.
        //    For a pin pointing at ANOTHER proposal's revision, reads would now fall back to the
        //    originals while Apply would execute the foreign revision - a preview/apply divergence
        //    in a state where the two previously agreed (both used the foreign one).
        //  - Missing row: reads fall back to the original operations, while Apply REFUSES outright
        //    (InvalidOperation) rather than execute an unapproved set.
        // Both states are unreachable: nothing but Approve writes ApprovedRevisionId, and a revision
        // is cascade-owned by its proposal with no code path deleting one individually, so a pin can
        // neither point elsewhere nor dangle while its proposal is readable.
        if (proposal.ApprovedRevisionId is Guid approvedRevisionId)
            return refsForProposal.FirstOrDefault(reference => reference.Id == approvedRevisionId);

        // Unconditional latest: what the reviewer sees, and what approve would pin. Deterministic
        // because (ProposalId, RevisionNumber) is uniquely indexed.
        if (proposal.Status is ProposalStatus.PendingReview)
            return refsForProposal.MaxBy(reference => reference.RevisionNumber);

        if (proposal.Status is ProposalStatus.Rejected)
        {
            // Freeze the rejected proposal at decision time. DecidedAt is always set by Reject, but
            // treat a null defensively as "no cutoff" and fall back to the unconditional latest.
            if (proposal.DecidedAt is not DateTime decidedAt)
                return refsForProposal.MaxBy(reference => reference.RevisionNumber);

            // Compared in memory rather than relying on EF's SQLite provider to translate a
            // DateTimeOffset-vs-DateTime comparison.
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
    /// supplied proposal appears in the result. A winner row that vanishes between
    /// the two reads degrades to originals, matching the existing proposal DTO
    /// read. A present but malformed winner fails CLOSED rather than silently
    /// degrading to originals, so evidence never describes an operation set Apply
    /// would refuse. Note the escape shape differs from preview/Apply: those return a
    /// ValidationError Result the controller renders as 400, while this throw reaches
    /// UnhandledExceptionMiddleware as a 500. Unreachable through the API today --
    /// ProposalRevisionService validates every revision with this same parser before
    /// it is stored -- so only an out-of-band write (hand-edited SQLite, a future
    /// import path) can produce it.
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
                || !byRevisionId.TryGetValue(revisionId, out var revision))
            {
                continue;
            }

            if (!ProposalRevisionPayload.TryParseOperations(
                    proposal.Id,
                    revision.RevisedPayload,
                    out var revisedOperations,
                    out var errorMessage))
            {
                throw new DomainException(ErrorCodes.ValidationError, errorMessage);
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
