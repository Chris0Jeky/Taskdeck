using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface INotificationRepository : IRepository<Notification>
{
    Task<IEnumerable<Notification>> GetByUserIdAsync(
        Guid userId,
        int limit = 100,
        bool unreadOnly = false,
        Guid? boardId = null,
        CancellationToken cancellationToken = default,
        int offset = 0);

    Task<Notification?> GetByUserAndDeduplicationKeyAsync(
        Guid userId,
        string deduplicationKey,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<Notification>> GetUnreadByUserIdAsync(
        Guid userId,
        Guid? boardId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all notifications for a user in batched SQL DELETEs to avoid
    /// unbounded memory and N+1 single-row deletes.
    /// </summary>
    /// <returns>Total number of deleted rows.</returns>
    Task<int> DeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
}
