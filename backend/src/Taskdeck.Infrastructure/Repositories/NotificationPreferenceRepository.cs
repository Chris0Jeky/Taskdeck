using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class NotificationPreferenceRepository : Repository<NotificationPreference>, INotificationPreferenceRepository
{
    public NotificationPreferenceRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<NotificationPreference?> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
    }
}
