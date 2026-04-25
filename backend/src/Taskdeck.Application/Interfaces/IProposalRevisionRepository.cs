using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IProposalRevisionRepository : IRepository<ProposalRevision>
{
    /// <summary>
    /// Gets all revisions for a proposal, ordered by revision number ascending.
    /// </summary>
    Task<IReadOnlyList<ProposalRevision>> GetByProposalIdAsync(Guid proposalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the latest (highest revision number) revision for a proposal, or null if none exist.
    /// </summary>
    Task<ProposalRevision?> GetLatestByProposalIdAsync(Guid proposalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the next revision number for a proposal (max existing + 1, or 1 if none exist).
    /// </summary>
    Task<int> GetNextRevisionNumberAsync(Guid proposalId, CancellationToken cancellationToken = default);
}
