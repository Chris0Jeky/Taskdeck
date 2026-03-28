using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class AgentProfileRepository : Repository<AgentProfile>, IAgentProfileRepository
{
    public AgentProfileRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<AgentProfile>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(ap => ap.UserId == userId)
            .OrderByDescending(ap => ap.CreatedAt)
            .ToListAsync(cancellationToken);
    }
}
