using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public sealed class WorkspacePlanRepository(TaskdeckDbContext db, IUserPreferenceRepository preferences) : IWorkspacePlanRepository
{
    public Task<UserPreference> GetAsync(Guid userId, CancellationToken ct) => preferences.GetOrCreateDefaultByUserIdAsync(userId, ct);

    public async Task<bool> SaveAsync(UserPreference preference, long expectedRevision, CancellationToken ct)
    {
        db.Entry(preference).Property(x => x.PersonalPlanRevision).OriginalValue = expectedRevision;
        try { await db.SaveChangesAsync(ct); return true; }
        catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); return false; }
    }
}
