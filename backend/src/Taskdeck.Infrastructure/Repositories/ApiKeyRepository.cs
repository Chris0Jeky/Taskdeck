using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class ApiKeyRepository : Repository<ApiKey>, IApiKeyRepository
{
    public ApiKeyRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<ApiKey?> GetByKeyHashAsync(string keyHash, CancellationToken cancellationToken = default)
    {
        return await _context.ApiKeys
            .FirstOrDefaultAsync(k => k.KeyHash == keyHash, cancellationToken);
    }

    public async Task<IEnumerable<ApiKey>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var keys = await _context.ApiKeys
            .Where(k => k.UserId == userId)
            .ToListAsync(cancellationToken);

        // Order client-side: SQLite does not support DateTimeOffset in ORDER BY.
        return keys.OrderByDescending(k => k.CreatedAt);
    }
}
