using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IProposalProvenanceRepository : IRepository<ProposalProvenance>
{
    /// <summary>
    /// Returns the provenance chain for a given proposal, including its fields
    /// and evidence links, or null if no provenance exists for the proposal.
    /// </summary>
    Task<ProposalProvenance?> GetByProposalIdAsync(Guid proposalId, CancellationToken cancellationToken = default);

    Task<int> DeleteEvidenceLinksBySourceIdsAsync(
        string sourceType,
        IReadOnlyCollection<Guid> sourceIds,
        CancellationToken cancellationToken = default);
}
