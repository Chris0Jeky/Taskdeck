using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class MfaCredentialRepository : Repository<MfaCredential>, IMfaCredentialRepository
{
    public MfaCredentialRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<MfaCredential?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.MfaCredentials
            .FirstOrDefaultAsync(e => e.UserId == userId, cancellationToken);
    }

    public async Task DeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var existing = await _context.MfaCredentials
            .Where(e => e.UserId == userId)
            .ToListAsync(cancellationToken);

        if (existing.Count > 0)
        {
            _context.MfaCredentials.RemoveRange(existing);
        }
    }
}
