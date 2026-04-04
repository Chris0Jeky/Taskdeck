using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IChatMessageRepository : IRepository<ChatMessage>
{
    Task<IEnumerable<ChatMessage>> GetBySessionIdAsync(Guid sessionId, int limit = 100, CancellationToken cancellationToken = default);
    Task<IEnumerable<ChatMessage>> GetByProposalIdAsync(Guid proposalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns message counts keyed by session ID for the given set of session IDs.
    /// Used by data export to avoid an N+1 query when counting messages across many sessions.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> CountBySessionIdsAsync(IEnumerable<Guid> sessionIds, CancellationToken cancellationToken = default);
}
