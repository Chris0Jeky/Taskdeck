using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IChatSessionRepository : IRepository<ChatSession>
{
    Task<IEnumerable<ChatSession>> GetByUserIdAsync(string userId, int limit = 100, CancellationToken cancellationToken = default);
    Task<IEnumerable<ChatSession>> GetByBoardIdAsync(string boardId, int limit = 100, CancellationToken cancellationToken = default);
    Task<IEnumerable<ChatSession>> GetByStatusAsync(ChatSessionStatus status, int limit = 100, CancellationToken cancellationToken = default);
    Task<ChatSession?> GetByIdWithMessagesAsync(Guid id, CancellationToken cancellationToken = default);
}
