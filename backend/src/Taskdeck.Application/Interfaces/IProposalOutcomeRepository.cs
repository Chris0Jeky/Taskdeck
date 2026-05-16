using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Interfaces;

public interface IProposalOutcomeRepository : IRepository<ProposalOutcome>
{
    /// <summary>
    /// Gets the outcome for a specific proposal, if one exists.
    /// </summary>
    Task<ProposalOutcome?> GetByProposalIdAsync(Guid proposalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all outcomes for a given user, ordered by creation date descending.
    /// </summary>
    Task<IReadOnlyList<ProposalOutcome>> GetByUserIdAsync(Guid userId, int limit = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets outcomes filtered by decision type.
    /// </summary>
    Task<IReadOnlyList<ProposalOutcome>> GetByDecisionAsync(OutcomeDecision decision, int limit = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all outcomes for a user without server-side ordering (safe for SQLite).
    /// </summary>
    Task<IReadOnlyList<ProposalOutcome>> GetAllByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
}
