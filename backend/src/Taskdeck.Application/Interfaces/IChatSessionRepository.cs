using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IChatSessionRepository : IRepository<ChatSession>
{
    Task<IEnumerable<ChatSession>> GetByUserIdAsync(Guid userId, int limit = 100, CancellationToken cancellationToken = default);
    Task<IEnumerable<ChatSession>> GetByBoardIdAsync(Guid boardId, int limit = 100, CancellationToken cancellationToken = default);
    Task<IEnumerable<ChatSession>> GetByStatusAsync(ChatSessionStatus status, int limit = 100, CancellationToken cancellationToken = default);
    Task<ChatSession?> GetByIdWithMessagesAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> TryBindBoardAsync(
        Guid sessionId,
        Guid userId,
        Guid boardId,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Set-based delete of every session owned by the user. Messages cascade at the
    /// database (required FK, DeleteBehavior.Cascade) and need no separate delete.
    /// Returns the number of deleted rows.
    /// </summary>
    Task<int> DeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
}
