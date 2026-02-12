using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IChatMessageRepository : IRepository<ChatMessage>
{
    Task<IEnumerable<ChatMessage>> GetBySessionIdAsync(string sessionId, int limit = 100, CancellationToken cancellationToken = default);
    Task<IEnumerable<ChatMessage>> GetByProposalIdAsync(string proposalId, CancellationToken cancellationToken = default);
}
