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

    public async Task<bool> TryConsumeAtomicAsync(string code, CancellationToken cancellationToken = default)
    {
        // Atomic UPDATE ensures only one concurrent request can consume a code.
        // The WHERE clause filters on IsConsumed = 0 so the second requester gets 0 affected rows.
        var now = DateTimeOffset.UtcNow;
        var affected = await _context.Database.ExecuteSqlRawAsync(
            "UPDATE OAuthAuthCodes SET IsConsumed = 1, ConsumedAt = {0}, UpdatedAt = {1} WHERE Code = {2} AND IsConsumed = 0",
            now.ToString("o"),
            now.ToString("o"),
            code);

        return affected > 0;
    }

    public async Task<int> DeleteExpiredAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        // EF Core 8 with SQLite cannot translate DateTimeOffset comparisons in LINQ
        // queries. Load all codes and filter in memory for cleanup.
        // Auth codes are short-lived and few in number, so this is acceptable.
        var allCodes = await _context.Set<OAuthAuthCode>()
            .ToListAsync(cancellationToken);

        var expired = allCodes.Where(e => e.ExpiresAt < cutoff).ToList();

        if (expired.Count == 0)
            return 0;

        _context.Set<OAuthAuthCode>().RemoveRange(expired);
        await _context.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }
}
