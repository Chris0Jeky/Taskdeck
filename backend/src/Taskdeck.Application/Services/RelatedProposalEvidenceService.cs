using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Services;

/// <summary>
/// Pages authorization-scoped proposal candidates, resolves each page's effective
/// operation sets in one batched revision read, then applies target/action filters.
/// Operation predicates deliberately never run in the repository: doing so would
/// permanently discard matches introduced by a revision (#2452).
/// </summary>
public sealed class RelatedProposalEvidenceService(
    IProposalEvidenceCandidateStore candidates,
    IProposalRevisionRepository revisions) : IRelatedProposalEvidenceService
{
    private const int CandidatePageSize = 100;

    public async Task<bool> HasOtherPendingProposalTargetingCardAsync(
        ProposalEvidenceScope scope,
        Guid excludedProposalId,
        Guid cardId,
        CancellationToken cancellationToken = default)
    {
        Validate(scope, excludedProposalId, cardId);

        var offset = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await candidates.ReadPendingPageAsync(
                scope, offset, CandidatePageSize, cancellationToken);
            if (page.Count == 0)
                return false;

            var operations = await ProposalEffectiveOperationsResolver.ResolveAsync(
                revisions, page, cancellationToken);
            if (page.Any(proposal =>
                    proposal.Id != excludedProposalId
                    && TargetsCard(operations[proposal.Id], cardId)))
            {
                return true;
            }

            if (page.Count < CandidatePageSize)
                return false;
            offset += page.Count;
        }
    }

    public async Task<AutomationProposal?> GetLatestOtherProposalTargetingCardAsync(
        ProposalEvidenceScope scope,
        Guid excludedProposalId,
        Guid cardId,
        CancellationToken cancellationToken = default)
    {
        Validate(scope, excludedProposalId, cardId);

        var offset = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await candidates.ReadHistoryPageAsync(
                scope, offset, CandidatePageSize, cancellationToken);
            if (page.Count == 0)
                return null;

            var operations = await ProposalEffectiveOperationsResolver.ResolveAsync(
                revisions, page, cancellationToken);
            var match = page.FirstOrDefault(proposal =>
                proposal.Id != excludedProposalId
                && TargetsCard(operations[proposal.Id], cardId));
            if (match is not null)
                return match;

            if (page.Count < CandidatePageSize)
                return null;
            offset += page.Count;
        }
    }

    public async Task<IReadOnlyList<AutomationProposal>> GetTerminalProposalsByEffectiveActionAsync(
        ProposalEvidenceScope scope,
        Guid excludedProposalId,
        string actionType,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (scope.RequestedByUserId == Guid.Empty)
            throw new ArgumentException("Evidence scope requires a requesting user.", nameof(scope));
        if (excludedProposalId == Guid.Empty)
            throw new ArgumentException("Excluded proposal ID cannot be empty.", nameof(excludedProposalId));
        if (string.IsNullOrWhiteSpace(actionType) || limit <= 0)
            return Array.Empty<AutomationProposal>();

        // The existing similar-past contract calls this with LookbackLimit=200.
        // That value bounds terminal DECISIONS INSPECTED, not matching results:
        // widening the scan until 200 matches would silently turn a bounded
        // evidence read into an unbounded account-history traversal.
        var inspected = 0;
        var matches = new List<AutomationProposal>(Math.Min(limit, CandidatePageSize));
        while (inspected < limit)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pageLimit = Math.Min(CandidatePageSize, limit - inspected);
            var page = await candidates.ReadTerminalPageAsync(
                scope, inspected, pageLimit, cancellationToken);
            if (page.Count == 0)
                break;

            var operations = await ProposalEffectiveOperationsResolver.ResolveAsync(
                revisions, page, cancellationToken);
            foreach (var proposal in page)
            {
                if (proposal.Id == excludedProposalId)
                    continue;

                var primaryAction = PrimaryAction(operations[proposal.Id]);
                if (string.Equals(primaryAction, actionType, StringComparison.OrdinalIgnoreCase))
                    matches.Add(proposal);
            }

            inspected += page.Count;
            if (page.Count < pageLimit)
                break;
        }

        return matches;
    }

    private static void Validate(
        ProposalEvidenceScope scope,
        Guid excludedProposalId,
        Guid cardId)
    {
        if (scope.RequestedByUserId == Guid.Empty)
            throw new ArgumentException("Evidence scope requires a requesting user.", nameof(scope));
        if (excludedProposalId == Guid.Empty)
            throw new ArgumentException("Excluded proposal ID cannot be empty.", nameof(excludedProposalId));
        if (cardId == Guid.Empty)
            throw new ArgumentException("Card ID cannot be empty.", nameof(cardId));
    }

    private static bool TargetsCard(
        IReadOnlyList<ProposalOperationDto> operations,
        Guid cardId) =>
        operations.Any(operation =>
            operation.TargetType.Equals("card", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(operation.TargetId, out var targetId)
            && targetId == cardId);

    private static string? PrimaryAction(IReadOnlyList<ProposalOperationDto> operations) =>
        operations.Count == 0
            ? null
            : operations.OrderBy(operation => operation.Sequence).First().ActionType;
}
