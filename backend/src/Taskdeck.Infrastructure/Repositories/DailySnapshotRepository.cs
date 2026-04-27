using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class DailySnapshotRepository : Repository<DailySnapshot>, IDailySnapshotRepository
{
    public DailySnapshotRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<DailySnapshot?> GetByUserAndDateAsync(Guid userId, DateOnly date, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .FirstOrDefaultAsync(ds => ds.UserId == userId && ds.Date == date, cancellationToken);
    }

    public async Task<IReadOnlyList<DailySnapshot>> GetSealedDaysAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(ds => ds.UserId == userId && ds.SealedAt != null && ds.Date >= from && ds.Date <= to)
            .OrderBy(ds => ds.Date)
            .ToListAsync(cancellationToken);
    }
}
