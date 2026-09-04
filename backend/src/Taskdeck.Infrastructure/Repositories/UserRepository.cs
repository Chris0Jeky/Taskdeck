using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

/// <summary>
/// SCAFFOLDING: Placeholder repository implementation for User entity.
/// </summary>
public class UserRepository : Repository<User>, IUserRepository
{
    public UserRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetUsernamesByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        var uniqueIds = ids
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (uniqueIds.Count == 0)
            return new Dictionary<Guid, string>();

        return await _context.Users
            .AsNoTracking()
            .Where(user => uniqueIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.Username, cancellationToken);
    }

    public async Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u => u.Username == username, cancellationToken);
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u => u.Email == email.ToLowerInvariant(), cancellationToken);
    }

    public async Task<bool> ExistsAsync(string username, string email, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .AnyAsync(u => u.Username == username || u.Email == email.ToLowerInvariant(), cancellationToken);
    }
}
