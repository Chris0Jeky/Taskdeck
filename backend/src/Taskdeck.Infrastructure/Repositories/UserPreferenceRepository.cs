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

        try
        {
            await AddAsync(defaultPreference, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            return defaultPreference;
        }
        catch (DbUpdateException ex)
        {
            if (!IsUniqueConstraintViolation(ex))
            {
                throw;
            }

            _context.Entry(defaultPreference).State = EntityState.Detached;
        }

        return await GetByUserIdAsync(userId, cancellationToken) ?? defaultPreference;
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        Exception? current = exception;

        while (current is not null)
        {
            var message = current.Message;
            if (!string.IsNullOrWhiteSpace(message) &&
                (message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("UNIQUE KEY constraint", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) ||
                 message.Contains("unique constraint", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            current = current.InnerException;
        }

        return false;
    }
}
