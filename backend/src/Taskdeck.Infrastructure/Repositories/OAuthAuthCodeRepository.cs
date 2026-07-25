using System.Globalization;
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
        // Bug boundary (issue #1403) is the COMPARISON side only: pass `now` as a DateTimeOffset
        // parameter so EF's SQLite provider serializes it with the same "yyyy-MM-dd HH:mm:ss.FFFFFFFzzz"
        // mapping (trailing fraction zeros trimmed, dot dropped at a zero fraction) that stored ExpiresAt
        // rows use, keeping `ExpiresAt > now` a chronological comparison at a zero-fraction tick instead
        // of a fixed-width string mismatch.
        //
        // The WRITE side (ConsumedAt/UpdatedAt) deliberately keeps the fixed-width invariant string:
        // issue #1403 notes the write side is unaffected (fixed-width strings parse fine), and the
        // #1393 MED-B1 regression test pins the persisted TEXT to the invariant "...ss.fffffff+00:00"
        // shape. `now` is UtcNow (zero offset) so .UtcDateTime normalization is a no-op today but keeps
        // the written string correct if the source ever changes.
        var now = DateTimeOffset.UtcNow;
        var nowStr = now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture) + "+00:00";
        var affected = await _context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE OAuthAuthCodes SET IsConsumed = 1, ConsumedAt = {nowStr}, UpdatedAt = {nowStr} WHERE Code = {code} AND IsConsumed = 0 AND ExpiresAt > {now}",
            cancellationToken);

        return affected > 0;
    }

    public async Task<int> DeleteExpiredAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        // Use raw SQL to avoid loading all rows into memory (DoS risk with large tables).
        // Deletes both expired codes AND consumed codes to prevent unbounded table growth.
        // Pass the DateTimeOffset cutoff as a parameter (not a hand-built ".fffffff" string): EF's
        // SQLite provider serializes both the parameter and the stored ExpiresAt column with the same
        // "yyyy-MM-dd HH:mm:ss.FFFFFFFzzz" mapping (trailing fraction zeros trimmed, dot dropped at a
        // zero fraction). A code expiring at exactly the cutoff with a zero-fraction tick then compares
        // EQUAL, so the strictly-older `ExpiresAt < cutoff` contract KEEPS it instead of deleting it —
        // a fixed-width bound sorts above the stored "...ss+00:00" because '+' (0x2B) < '.' (0x2E)
        // (issue #1403). Mirrors AuditLogRepository.DeleteOldEntriesAsync.
        //
        // Normalize to UTC first: EF's SQLite mapping PRESERVES the offset (it does not convert to UTC),
        // so a non-UTC cutoff (e.g. -05:00) would serialize with a "-05:00" suffix and its shifted
        // wall-clock digits would compare against the stored "+00:00" rows as a raw string rather than by
        // instant. ToUniversalTime() yields the same instant at "+00:00" so TEXT order equals chrono order.
        var cutoffUtc = cutoff.ToUniversalTime();
        var affected = await _context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM OAuthAuthCodes WHERE ExpiresAt < {cutoffUtc} OR IsConsumed = 1",
            cancellationToken);

        return affected;
    }
}
