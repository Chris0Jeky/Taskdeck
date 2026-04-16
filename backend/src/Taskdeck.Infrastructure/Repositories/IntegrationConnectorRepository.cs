using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public sealed class IntegrationConnectorRepository : Repository<IntegrationConnector>, IIntegrationConnectorRepository
{
    public IntegrationConnectorRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<IntegrationConnector>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await _context.IntegrationConnectors
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IntegrationConnector?> GetByIdForUserAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await _context.IntegrationConnectors
            .FirstOrDefaultAsync(
                c => c.Id == id && c.UserId == userId,
                cancellationToken);
    }
}
