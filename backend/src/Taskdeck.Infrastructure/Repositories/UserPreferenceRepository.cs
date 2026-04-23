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

    public async Task<UserPreference> GetOrCreateDefaultByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var existingPreference = await GetByUserIdAsync(userId, cancellationToken);
        if (existingPreference is not null)
        {
            return existingPreference;
        }

        var defaultPreference = UserPreference.CreateDefault(userId);
        var now = defaultPreference.CreatedAt;

        // Use INSERT OR IGNORE to atomically skip the insert when a concurrent
        // request has already created the row, avoiding UNIQUE-constraint
        // exceptions and the retry loop they previously required.
        object[] parameters =
        [
            defaultPreference.Id,
            userId,
            defaultPreference.WorkspaceMode.ToString(),
            defaultPreference.OnboardingVisibility.ToString(),
            (object?)defaultPreference.OnboardingDismissedAt ?? DBNull.Value,
            (object?)defaultPreference.OnboardingCompletedAt ?? DBNull.Value,
            now,
            now
        ];

        await _context.Database.ExecuteSqlRawAsync(
            @"INSERT OR IGNORE INTO UserPreferences
              (Id, UserId, WorkspaceMode, OnboardingVisibility,
               OnboardingDismissedAt, OnboardingCompletedAt, CreatedAt, UpdatedAt)
              VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7})",
            parameters,
            cancellationToken);

        // The row now exists -- either we just inserted it or another request
        // did. Query it back so EF Core's change tracker is aware of it.
        var preference = await GetByUserIdAsync(userId, cancellationToken);
        return preference!;
    }
}
