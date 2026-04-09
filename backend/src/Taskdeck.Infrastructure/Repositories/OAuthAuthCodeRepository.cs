using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class OAuthAuthCodeRepository : Repository<OAuthAuthCode>, IOAuthAuthCodeRepository
{
    public OAuthAuthCodeRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<OAuthAuthCode?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        return await _context.Set<OAuthAuthCode>()
            .FirstOrDefaultAsync(e => e.Code == code, cancellationToken);
    }

    public async Task<int> DeleteExpiredAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        return await _context.Set<OAuthAuthCode>()
            .Where(e => e.ExpiresAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
