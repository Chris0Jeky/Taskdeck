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
        // The WHERE clause enforces ALL invariants atomically:
        //   - IsConsumed = 0: single-use semantics (second requester gets 0 rows)
        //   - ExpiresAt > now: prevents TOCTOU race where expiry passes between
        //     the application-level check and the SQL execution
        //
        // EF Core SQLite stores DateTimeOffset as "yyyy-MM-dd HH:mm:ss.fffffff+HH:mm" format.
        // We must use the same format for string comparison to work correctly.
        var now = DateTimeOffset.UtcNow;
        var nowStr = now.ToString("yyyy-MM-dd HH:mm:ss.fffffff+00:00");
        var affected = await _context.Database.ExecuteSqlRawAsync(
            "UPDATE OAuthAuthCodes SET IsConsumed = 1, ConsumedAt = {0}, UpdatedAt = {1} WHERE Code = {2} AND IsConsumed = 0 AND ExpiresAt > {3}",
            nowStr, nowStr, code, nowStr);

        return affected > 0;
    }

    public async Task<int> DeleteExpiredAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        // Use raw SQL to avoid loading all rows into memory (DoS risk with large tables).
        // Deletes both expired codes AND consumed codes to prevent unbounded table growth.
        // EF Core SQLite stores DateTimeOffset as "yyyy-MM-dd HH:mm:ss.fffffff+HH:mm".
        var cutoffStr = cutoff.ToString("yyyy-MM-dd HH:mm:ss.fffffff+00:00");
        var affected = await _context.Database.ExecuteSqlRawAsync(
            "DELETE FROM OAuthAuthCodes WHERE ExpiresAt < {0} OR IsConsumed = 1",
            cutoffStr);

        return affected;
    }
}
