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
}
