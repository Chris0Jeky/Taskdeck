using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class UserPreferenceRepository : Repository<UserPreference>, IUserPreferenceRepository
{
    public UserPreferenceRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<UserPreference?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.Set<UserPreference>()
            .FirstOrDefaultAsync(preference => preference.UserId == userId, cancellationToken);
    }
}
