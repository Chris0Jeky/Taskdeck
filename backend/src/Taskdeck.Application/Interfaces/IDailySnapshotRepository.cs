using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IDailySnapshotRepository : IRepository<DailySnapshot>
{
    Task<DailySnapshot?> GetByUserAndDateAsync(Guid userId, DateOnly date, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DailySnapshot>> GetSealedDaysAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}
