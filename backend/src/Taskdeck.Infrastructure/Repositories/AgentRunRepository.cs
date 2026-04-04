using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class AgentRunRepository : Repository<AgentRun>, IAgentRunRepository
{
    private const int DefaultLimit = 100;

    public AgentRunRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<AgentRun>> GetByAgentProfileIdAsync(Guid agentProfileId, int limit = 100, CancellationToken cancellationToken = default)
    {
        var boundedLimit = limit <= 0 ? DefaultLimit : limit;

        // Materialize first, then sort in memory — SQLite doesn't support DateTimeOffset in ORDER BY
        var runs = await _dbSet
            .Where(ar => ar.AgentProfileId == agentProfileId)
            .ToListAsync(cancellationToken);
        return runs
            .OrderByDescending(ar => ar.CreatedAt)
            .Take(boundedLimit);
    }

    public async Task<AgentRun?> GetByIdWithEventsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(ar => ar.Events)
            .FirstOrDefaultAsync(ar => ar.Id == id, cancellationToken);
    }
}
