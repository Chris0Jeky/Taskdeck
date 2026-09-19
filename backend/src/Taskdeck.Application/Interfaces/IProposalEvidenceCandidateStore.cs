using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

/// <summary>
/// Authorization-preserving scope for related proposal evidence. Board-backed
/// proposals share board history; board-less proposals share only their owner's
/// private history.
/// </summary>
public readonly record struct ProposalEvidenceScope(Guid? BoardId, Guid RequestedByUserId);

/// <summary>
/// Reads deterministic, bounded pages of proposal candidates without filtering
/// by creation-time operations. Effective revision filtering belongs in
/// Application after these candidates are materialized (#2452).
/// </summary>
public interface IProposalEvidenceCandidateStore
{
    Task<IReadOnlyList<AutomationProposal>> ReadPendingPageAsync(
        ProposalEvidenceScope scope,
        int offset,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AutomationProposal>> ReadHistoryPageAsync(
        ProposalEvidenceScope scope,
        int offset,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AutomationProposal>> ReadTerminalPageAsync(
        ProposalEvidenceScope scope,
        int offset,
        int limit,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves related proposal evidence from each candidate's effective operation
/// set: approved pin, latest pending revision, rejected decision-time revision,
/// or original operations when no revision applies.
/// </summary>
public interface IRelatedProposalEvidenceService
{
    Task<bool> HasOtherPendingProposalTargetingCardAsync(
        ProposalEvidenceScope scope,
        Guid excludedProposalId,
        Guid cardId,
        CancellationToken cancellationToken = default);

    Task<AutomationProposal?> GetLatestOtherProposalTargetingCardAsync(
        ProposalEvidenceScope scope,
        Guid excludedProposalId,
        Guid cardId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AutomationProposal>> GetTerminalProposalsByEffectiveActionAsync(
        ProposalEvidenceScope scope,
        Guid excludedProposalId,
        string actionType,
        int limit,
        CancellationToken cancellationToken = default);
}
