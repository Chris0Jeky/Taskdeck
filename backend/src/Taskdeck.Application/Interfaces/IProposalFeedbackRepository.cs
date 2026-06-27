using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IProposalFeedbackRepository : IRepository<ProposalFeedback>
{
    /// <summary>
    /// Gets the single feedback row for a (proposal, user) pair, or null if the user has not
    /// reported this proposal. Tracked (not AsNoTracking) so the caller can refine the reason
    /// in place. Backs the idempotent-report pre-check.
    /// </summary>
    Task<ProposalFeedback?> GetByProposalAndUserAsync(Guid proposalId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a user's feedback rows, newest first, capped for cohort reads.
    /// </summary>
    Task<IReadOnlyList<ProposalFeedback>> GetAllByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the COMPLETE set of a user's feedback rows, newest first and UNCAPPED, for the
    /// data-portability export. The cohort read (<see cref="GetAllByUserIdAsync"/>) is capped at a
    /// 1000-row sample, which would silently truncate a heavy reporter's export; portability must
    /// be complete, so this read carries no limit.
    /// </summary>
    Task<IReadOnlyList<ProposalFeedback>> GetAllByUserIdForExportAsync(Guid userId, CancellationToken cancellationToken = default);
}
