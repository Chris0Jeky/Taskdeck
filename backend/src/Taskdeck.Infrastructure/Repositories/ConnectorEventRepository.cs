using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public sealed class ConnectorEventRepository : Repository<ConnectorEvent>, IConnectorEventRepository
{
    public ConnectorEventRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<ConnectorEvent>> GetRecentByConnectorIdAsync(
        Guid connectorId,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (_context.Database.IsSqlite())
        {
            return await _context.ConnectorEvents
                .FromSqlInterpolated(
                    $"SELECT * FROM ConnectorEvents WHERE ConnectorId = {connectorId} ORDER BY CreatedAt DESC LIMIT {limit}")
                .ToListAsync(cancellationToken);
        }

        return await _context.ConnectorEvents
            .Where(e => e.ConnectorId == connectorId)
            .OrderByDescending(e => e.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
