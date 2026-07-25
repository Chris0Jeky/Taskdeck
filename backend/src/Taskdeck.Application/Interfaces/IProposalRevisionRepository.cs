using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

/// <summary>
/// Metadata-only view of a <see cref="ProposalRevision"/>: exactly the columns the
/// effective-revision rules need to decide WHICH revision wins, and nothing else.
/// <para>
/// Deliberately excludes <c>RevisedPayload</c> (#1444 review). The payload is unbounded — it has no
/// <c>HasMaxLength</c>, and a proposal's revision count is uncapped — so loading whole revision rows
/// just to compare revision numbers and timestamps turns a bounded list read into an unbounded
/// row × bytes fanout. Callers select over refs, then fetch the full row for the winner alone.
/// </para>
/// </summary>
public sealed record ProposalRevisionRef(Guid Id, Guid ProposalId, int RevisionNumber, DateTimeOffset RevisedAt);

public interface IProposalRevisionRepository : IRepository<ProposalRevision>
{
    /// <summary>
    /// Gets all revisions for a proposal, ordered by revision number ascending.
    /// </summary>
    Task<IReadOnlyList<ProposalRevision>> GetByProposalIdAsync(Guid proposalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets metadata-only <see cref="ProposalRevisionRef"/>s for every revision belonging to the
    /// supplied proposals, so an effective revision can be resolved for a whole page of proposals
    /// without a per-proposal query AND without reading any revision payload (#1444).
    /// <para>
    /// Ordering guarantee: refs for the SAME proposal appear in ascending revision-number order. The
    /// relative order of different proposals is unspecified, because the implementation may read in
    /// chunks — callers are expected to group by <see cref="ProposalRevisionRef.ProposalId"/>.
    /// </para>
    /// Returns an empty list when <paramref name="proposalIds"/> is empty. Duplicate ids are
    /// tolerated and do not duplicate rows.
    /// </summary>
    Task<IReadOnlyList<ProposalRevisionRef>> GetRefsByProposalIdsAsync(IEnumerable<Guid> proposalIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the full revisions (payload included) for the supplied revision ids. Paired with
    /// <see cref="GetRefsByProposalIdsAsync"/>: refs decide which revisions win, then this loads only
    /// those — at most one per proposal — instead of every revision on the page (#1444).
    /// <para>
    /// Returns an empty list when <paramref name="revisionIds"/> is empty. Ids that do not exist are
    /// simply absent from the result. Result ordering is unspecified; callers index by id.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<ProposalRevision>> GetByIdsAsync(IEnumerable<Guid> revisionIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the latest (highest revision number) revision for a proposal, or null if none exist.
    /// </summary>
    Task<ProposalRevision?> GetLatestByProposalIdAsync(Guid proposalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the next revision number for a proposal (max existing + 1, or 1 if none exist).
    /// </summary>
    Task<int> GetNextRevisionNumberAsync(Guid proposalId, CancellationToken cancellationToken = default);
}
