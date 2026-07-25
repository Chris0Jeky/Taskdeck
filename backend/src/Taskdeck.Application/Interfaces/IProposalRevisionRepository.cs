using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IProposalRevisionRepository : IRepository<ProposalRevision>
{
    /// <summary>
    /// Gets all revisions for a proposal, ordered by revision number ascending.
    /// </summary>
    Task<IReadOnlyList<ProposalRevision>> GetByProposalIdAsync(Guid proposalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all revisions belonging to the supplied proposals. Batch companion to
    /// <see cref="GetByProposalIdAsync"/> for reads that must resolve an effective revision for a
    /// whole page of proposals without a per-proposal query (#1444).
    /// <para>
    /// Ordering guarantee: revisions of the SAME proposal appear in ascending revision-number order.
    /// The relative order of different proposals is unspecified, because the implementation may read
    /// in chunks — callers are expected to group by <see cref="ProposalRevision.ProposalId"/>.
    /// </para>
    /// Returns an empty list when <paramref name="proposalIds"/> is empty. Duplicate ids are
    /// tolerated and do not duplicate rows.
    /// </summary>
    Task<IReadOnlyList<ProposalRevision>> GetByProposalIdsAsync(IEnumerable<Guid> proposalIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the latest (highest revision number) revision for a proposal, or null if none exist.
    /// </summary>
    Task<ProposalRevision?> GetLatestByProposalIdAsync(Guid proposalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the next revision number for a proposal (max existing + 1, or 1 if none exist).
    /// </summary>
    Task<int> GetNextRevisionNumberAsync(Guid proposalId, CancellationToken cancellationToken = default);
}
