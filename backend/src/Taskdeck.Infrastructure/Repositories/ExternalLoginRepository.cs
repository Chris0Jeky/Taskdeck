using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class ExternalLoginRepository : Repository<ExternalLogin>, IExternalLoginRepository
{
    public ExternalLoginRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<ExternalLogin?> GetByProviderAsync(string provider, string providerUserId, CancellationToken cancellationToken = default)
    {
        return await _context.ExternalLogins
            .FirstOrDefaultAsync(e => e.Provider == provider && e.ProviderUserId == providerUserId, cancellationToken);
    }

    public async Task<IEnumerable<ExternalLogin>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.ExternalLogins
            .Where(e => e.UserId == userId)
            .ToListAsync(cancellationToken);
    }
}
