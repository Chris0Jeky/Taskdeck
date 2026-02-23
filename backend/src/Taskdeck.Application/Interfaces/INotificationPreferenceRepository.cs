using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface INotificationPreferenceRepository : IRepository<NotificationPreference>
{
    Task<NotificationPreference?> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
