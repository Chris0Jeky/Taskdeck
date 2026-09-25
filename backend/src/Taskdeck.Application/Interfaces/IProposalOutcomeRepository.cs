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
    /// Gets the most recently decided outcomes for a user, ordered before the 1000-row safety cap.
    /// The SQLite implementation uses SQL ordering for its DateTimeOffset column.
    /// </summary>
    Task<IReadOnlyList<ProposalOutcome>> GetAllByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
}
