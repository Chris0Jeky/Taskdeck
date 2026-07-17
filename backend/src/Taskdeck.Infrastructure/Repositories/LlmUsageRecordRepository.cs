using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class LlmUsageRecordRepository : Repository<LlmUsageRecord>, ILlmUsageRecordRepository
{
    private const int StatusReserved = (int)LlmUsageRecordStatus.Reserved;
    private const int StatusCommitted = (int)LlmUsageRecordStatus.Committed;

    public LlmUsageRecordRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<long> GetRequestCountAsync(
        Guid? userId,
        LlmSurface? surface,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (_context.Database.IsSqlite())
        {
            return await GetRequestCountSqliteAsync(userId, surface, from, to, cancellationToken);
        }

        var query = BuildFilteredQuery(userId, surface, from, to);
        return await query.LongCountAsync(cancellationToken);
    }

    public async Task<long> GetTotalTokensAsync(
        Guid? userId,
        LlmSurface? surface,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (_context.Database.IsSqlite())
        {
            return await GetTotalTokensSqliteAsync(userId, surface, from, to, cancellationToken);
        }

        var query = BuildFilteredQuery(userId, surface, from, to);
        var hasAny = await query.AnyAsync(cancellationToken);
        if (!hasAny)
            return 0;

        return await query.SumAsync(r => (long)r.InputTokens + r.OutputTokens, cancellationToken);
    }

    public async Task<(long TotalInputTokens, long TotalOutputTokens, long TotalRequests)> GetUsageSummaryAsync(
        Guid? userId,
        LlmSurface? surface,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (_context.Database.IsSqlite())
        {
            return await GetUsageSummarySqliteAsync(userId, surface, from, to, cancellationToken);
        }

        var query = BuildFilteredQuery(userId, surface, from, to);
        var count = await query.LongCountAsync(cancellationToken);
        if (count == 0)
            return (0, 0, 0);

        var totalInput = await query.SumAsync(r => (long)r.InputTokens, cancellationToken);
        var totalOutput = await query.SumAsync(r => (long)r.OutputTokens, cancellationToken);

        return (totalInput, totalOutput, count);
    }

    // --- Atomic reservation (issue #1313) --------------------------------------------------------
    //
    // Quota was checked (aggregate read) and recorded (row insert) in two steps with an LLM network
    // call in between, so two concurrent callers could both pass on stale totals and overshoot the
    // boundary. The fix reserves a row up front and finalizes it after the call. The atomicity comes
    // from a single conditional INSERT ... SELECT ... WHERE statement: SQLite serializes writers (one
    // writer at a time, others wait out busy_timeout), so each statement's limit subqueries observe the
    // committed rows of any reservation that landed first — exactly one concurrent caller crosses the
    // boundary, the rest insert zero rows. This is provider-guaranteed and does not depend on manual
    // BEGIN/COMMIT transaction control (which Microsoft.Data.Sqlite does not honour via raw commands).
    // Reservations that outlive their TTL (a crashed process) are swept first and are also excluded from
    // every subquery by the `ExpiresAt > now` live predicate, so a stale row can neither block nor leak.

    public async Task<QuotaReservationOutcome> TryReserveAsync(
        Guid userId,
        LlmSurface surface,
        DateTimeOffset hourStart,
        DateTimeOffset now,
        DateTimeOffset dayStart,
        DateTimeOffset dayEnd,
        long requestsPerHour,
        long tokensPerDay,
        long globalBudgetCeilingTokens,
        int estimatedTokens,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        if (!_context.Database.IsSqlite())
        {
            return await TryReserveNonSqliteAsync(
                userId, surface, hourStart, now, dayStart, dayEnd,
                requestsPerHour, tokensPerDay, globalBudgetCeilingTokens,
                estimatedTokens, expiresAt, cancellationToken);
        }

        var surfaceValue = (int)surface;
        var provider = LlmUsageRecord.ReservationProvider;
        var model = string.Empty;

        // Atomic conditional insert: the reservation row is written only if every enabled limit still has
        // headroom against live (committed + non-expired reserved) usage, all evaluated inside the one
        // statement SQLite executes under the write lock. affected == 1 => reserved; 0 => denied. The
        // stale-reservation sweep (idempotent) runs first inside the same retried unit so a contended
        // write waits/retries rather than surfacing SQLITE_BUSY as a 500 (#1282 parity). A retried
        // attempt re-uses the same reservationId — a prior failed attempt inserted nothing, so no dup.
        var reservationId = Guid.NewGuid();
        var affected = await WithSqliteWriteRetryAsync(async () =>
        {
            // Sweep stale reservations (age-based expiry) so the table cannot grow without bound.
            // Correctness does not depend on it — the live predicate below already ignores expired rows.
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM LlmUsageRecords WHERE Status = {StatusReserved} AND ExpiresAt IS NOT NULL AND ExpiresAt <= {now}",
                cancellationToken);

            return await _context.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO LlmUsageRecords
(Id, UserId, Surface, Provider, Model, InputTokens, OutputTokens, Status, ExpiresAt, CreatedAt, UpdatedAt)
SELECT {reservationId}, {userId}, {surfaceValue}, {provider}, {model}, {estimatedTokens}, 0, {StatusReserved}, {expiresAt}, {now}, {now}
WHERE ({requestsPerHour} <= 0 OR (
        SELECT COUNT(*) FROM LlmUsageRecords
        WHERE UserId = {userId} AND Surface = {surfaceValue}
          AND CreatedAt >= {hourStart}
          AND (Status = {StatusCommitted} OR ExpiresAt > {now})) < {requestsPerHour})
  AND ({tokensPerDay} <= 0 OR (
        SELECT COALESCE(SUM(CAST(InputTokens AS INTEGER) + CAST(OutputTokens AS INTEGER)), 0) FROM LlmUsageRecords
        WHERE UserId = {userId} AND Surface = {surfaceValue}
          AND CreatedAt >= {dayStart} AND CreatedAt < {dayEnd}
          AND (Status = {StatusCommitted} OR ExpiresAt > {now})) < {tokensPerDay})
  AND ({globalBudgetCeilingTokens} <= 0 OR (
        SELECT COALESCE(SUM(CAST(InputTokens AS INTEGER) + CAST(OutputTokens AS INTEGER)), 0) FROM LlmUsageRecords
        WHERE Surface = {surfaceValue}
          AND CreatedAt >= {dayStart} AND CreatedAt < {dayEnd}
          AND (Status = {StatusCommitted} OR ExpiresAt > {now})) < {globalBudgetCeilingTokens})",
                cancellationToken);
        }, cancellationToken);

        // Read the live counts once for the outcome. On success they include the just-inserted
        // reservation (post-consumption headroom); on denial they identify which limit was hit for the
        // caller's error message. This read is only informational — the atomic decision already happened.
        // No `CreatedAt < now` upper bound: the reservation row is stamped at `now`, and a concurrent
        // reserver captures a near-identical `now`, so an exclusive upper bound would drop the very row
        // that must be counted to serialize the boundary. Rows are never in the future, so `>= hourStart`
        // is the correct last-hour window here.
        var requestCount = requestsPerHour > 0
            ? await LiveScalarAsync(
                $@"SELECT COUNT(*) AS Value FROM LlmUsageRecords
                   WHERE UserId = {userId} AND Surface = {surfaceValue}
                     AND CreatedAt >= {hourStart}
                     AND (Status = {StatusCommitted} OR ExpiresAt > {now})",
                cancellationToken)
            : 0;

        var userTokens = tokensPerDay > 0
            ? await LiveScalarAsync(
                $@"SELECT COALESCE(SUM(CAST(InputTokens AS INTEGER) + CAST(OutputTokens AS INTEGER)), 0) AS Value FROM LlmUsageRecords
                   WHERE UserId = {userId} AND Surface = {surfaceValue}
                     AND CreatedAt >= {dayStart} AND CreatedAt < {dayEnd}
                     AND (Status = {StatusCommitted} OR ExpiresAt > {now})",
                cancellationToken)
            : 0;

        var globalTokens = globalBudgetCeilingTokens > 0
            ? await LiveScalarAsync(
                $@"SELECT COALESCE(SUM(CAST(InputTokens AS INTEGER) + CAST(OutputTokens AS INTEGER)), 0) AS Value FROM LlmUsageRecords
                   WHERE Surface = {surfaceValue}
                     AND CreatedAt >= {dayStart} AND CreatedAt < {dayEnd}
                     AND (Status = {StatusCommitted} OR ExpiresAt > {now})",
                cancellationToken)
            : 0;

        if (affected > 0)
        {
            return new QuotaReservationOutcome(
                QuotaReservationDecision.Allowed, reservationId, requestCount, userTokens, globalTokens);
        }

        // Attribute the denial to whichever limit the re-read shows exceeded. If a concurrent release
        // raced the re-read so none reads as exceeded, fall back to the first *enabled* limit (never a
        // disabled one) so the caller never sees a message for a limit that is off.
        var decision = requestsPerHour > 0 && requestCount >= requestsPerHour
            ? QuotaReservationDecision.RequestsExceeded
            : tokensPerDay > 0 && userTokens >= tokensPerDay
                ? QuotaReservationDecision.TokensExceeded
                : globalBudgetCeilingTokens > 0 && globalTokens >= globalBudgetCeilingTokens
                    ? QuotaReservationDecision.GlobalExceeded
                    : requestsPerHour > 0
                        ? QuotaReservationDecision.RequestsExceeded
                        : tokensPerDay > 0
                            ? QuotaReservationDecision.TokensExceeded
                            : QuotaReservationDecision.GlobalExceeded;

        return new QuotaReservationOutcome(decision, null, requestCount, userTokens, globalTokens);
    }

    private async Task<long> LiveScalarAsync(FormattableString sql, CancellationToken cancellationToken)
    {
        return await _context.Database.SqlQuery<long>(sql).SingleAsync(cancellationToken);
    }

    private const int MaxSqliteWriteLockRetries = 5;

    // Mirrors UnitOfWork.SaveChangesAsync's transient-lock handling for the raw-SQL reservation writes:
    // a contended write waits and retries with backoff instead of surfacing SQLITE_BUSY as a 500 (#1282).
    private static async Task<T> WithSqliteWriteRetryAsync<T>(
        Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex) when (attempt < MaxSqliteWriteLockRetries && IsTransientSqliteWriteLock(ex))
            {
                var multiplier = attempt + 1;
                await Task.Delay(TimeSpan.FromMilliseconds(25 * multiplier * multiplier), cancellationToken);
            }
        }
    }

    private static bool IsTransientSqliteWriteLock(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqliteException sqliteException
                && (sqliteException.SqliteErrorCode == 5 || sqliteException.SqliteErrorCode == 6))
            {
                return true;
            }

            if (current.Message.Contains("database is locked", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("database table is locked", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public async Task<bool> CommitReservationAsync(
        Guid reservationId,
        string provider,
        string model,
        int inputTokens,
        int outputTokens,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var safeProvider = string.IsNullOrWhiteSpace(provider) ? LlmUsageRecord.ReservationProvider : provider;
        var safeModel = model ?? string.Empty;
        var safeInput = Math.Max(0, inputTokens);
        var safeOutput = Math.Max(0, outputTokens);

        // Single atomic UPDATE gated on Status = Reserved: idempotent against a double-commit and a
        // no-op if the row was already released or swept. Raw SQL keeps the caller's shared change
        // tracker (e.g. the chat message being composed) from flushing early. Retried on a transient
        // write lock for #1282 parity with the SaveChanges path this replaced.
        var affected = await WithSqliteWriteRetryAsync(
            () => _context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE LlmUsageRecords SET Status = {StatusCommitted}, ExpiresAt = NULL, Provider = {safeProvider}, Model = {safeModel}, InputTokens = {safeInput}, OutputTokens = {safeOutput}, UpdatedAt = {now} WHERE Id = {reservationId} AND Status = {StatusReserved}",
                cancellationToken),
            cancellationToken);

        return affected > 0;
    }

    public async Task<bool> ReleaseReservationAsync(
        Guid reservationId,
        CancellationToken cancellationToken = default)
    {
        var affected = await WithSqliteWriteRetryAsync(
            () => _context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM LlmUsageRecords WHERE Id = {reservationId} AND Status = {StatusReserved}",
                cancellationToken),
            cancellationToken);

        return affected > 0;
    }

    // Non-SQLite fallback (e.g. an in-memory relational provider in isolated tests). Best-effort:
    // relational providers other than SQLite are not part of the shared-file deployment model, so the
    // stricter BEGIN IMMEDIATE serialization is unnecessary; a check-then-insert is sufficient there.
    private async Task<QuotaReservationOutcome> TryReserveNonSqliteAsync(
        Guid userId,
        LlmSurface surface,
        DateTimeOffset hourStart,
        DateTimeOffset now,
        DateTimeOffset dayStart,
        DateTimeOffset dayEnd,
        long requestsPerHour,
        long tokensPerDay,
        long globalBudgetCeilingTokens,
        int estimatedTokens,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        var requestCount = requestsPerHour > 0
            ? await _dbSet.AsNoTracking()
                .Where(r => r.UserId == userId && r.Surface == surface
                    && r.CreatedAt >= hourStart && r.CreatedAt < now
                    && (r.Status == LlmUsageRecordStatus.Committed || r.ExpiresAt > now))
                .LongCountAsync(cancellationToken)
            : 0;

        if (requestsPerHour > 0 && requestCount >= requestsPerHour)
            return new QuotaReservationOutcome(QuotaReservationDecision.RequestsExceeded, null, requestCount, 0, 0);

        var userTokens = tokensPerDay > 0
            ? await _dbSet.AsNoTracking()
                .Where(r => r.UserId == userId && r.Surface == surface
                    && r.CreatedAt >= dayStart && r.CreatedAt < dayEnd
                    && (r.Status == LlmUsageRecordStatus.Committed || r.ExpiresAt > now))
                .SumAsync(r => (long)r.InputTokens + r.OutputTokens, cancellationToken)
            : 0;

        if (tokensPerDay > 0 && userTokens >= tokensPerDay)
            return new QuotaReservationOutcome(QuotaReservationDecision.TokensExceeded, null, requestCount, userTokens, 0);

        var globalTokens = globalBudgetCeilingTokens > 0
            ? await _dbSet.AsNoTracking()
                .Where(r => r.Surface == surface
                    && r.CreatedAt >= dayStart && r.CreatedAt < dayEnd
                    && (r.Status == LlmUsageRecordStatus.Committed || r.ExpiresAt > now))
                .SumAsync(r => (long)r.InputTokens + r.OutputTokens, cancellationToken)
            : 0;

        if (globalBudgetCeilingTokens > 0 && globalTokens >= globalBudgetCeilingTokens)
            return new QuotaReservationOutcome(QuotaReservationDecision.GlobalExceeded, null, requestCount, userTokens, globalTokens);

        var reservation = LlmUsageRecord.CreateReservation(userId, surface, estimatedTokens, expiresAt);
        await _dbSet.AddAsync(reservation, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return new QuotaReservationOutcome(
            QuotaReservationDecision.Allowed, reservation.Id, requestCount, userTokens, globalTokens);
    }

    // SQLite stores DateTimeOffset as ISO 8601 text. Use raw SQL with string
    // comparison so filtering is pushed to the database instead of loading the
    // entire table into memory.

    private async Task<long> GetRequestCountSqliteAsync(
        Guid? userId, LlmSurface? surface, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var (whereClauses, parameters) = BuildSqliteWhere(userId, surface, from, to);
        var sql = $"SELECT COUNT(*) AS Value FROM LlmUsageRecords WHERE {string.Join(" AND ", whereClauses)}";

        var result = await _context.Database
            .SqlQueryRaw<int>(sql, parameters.ToArray())
            .FirstAsync(ct);

        return result;
    }

    private async Task<long> GetTotalTokensSqliteAsync(
        Guid? userId, LlmSurface? surface, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var (whereClauses, parameters) = BuildSqliteWhere(userId, surface, from, to);
        var sql = $"SELECT COALESCE(SUM(CAST(InputTokens AS INTEGER) + CAST(OutputTokens AS INTEGER)), 0) AS Value FROM LlmUsageRecords WHERE {string.Join(" AND ", whereClauses)}";

        var result = await _context.Database
            .SqlQueryRaw<long>(sql, parameters.ToArray())
            .FirstAsync(ct);

        return result;
    }

    private async Task<(long, long, long)> GetUsageSummarySqliteAsync(
        Guid? userId, LlmSurface? surface, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var (whereClauses, parameters) = BuildSqliteWhere(userId, surface, from, to);
        var where = string.Join(" AND ", whereClauses);

        var countSql = $"SELECT COUNT(*) AS Value FROM LlmUsageRecords WHERE {where}";
        var count = await _context.Database
            .SqlQueryRaw<long>(countSql, parameters.ToArray())
            .FirstAsync(ct);

        if (count == 0)
            return (0, 0, 0);

        var inputSql = $"SELECT COALESCE(SUM(CAST(InputTokens AS INTEGER)), 0) AS Value FROM LlmUsageRecords WHERE {where}";
        var totalInput = await _context.Database
            .SqlQueryRaw<long>(inputSql, parameters.ToArray())
            .FirstAsync(ct);

        var outputSql = $"SELECT COALESCE(SUM(CAST(OutputTokens AS INTEGER)), 0) AS Value FROM LlmUsageRecords WHERE {where}";
        var totalOutput = await _context.Database
            .SqlQueryRaw<long>(outputSql, parameters.ToArray())
            .FirstAsync(ct);

        return (totalInput, totalOutput, count);
    }

    // EF Core SQLite stores DateTimeOffset as "yyyy-MM-dd HH:mm:ss.FFFFFFFzzz"
    // (space separator, not 'T') and Guid as uppercase text. Match both formats
    // so raw SQL string comparisons work correctly.
    private const string SqliteDateFormat = "yyyy-MM-dd HH:mm:ss.FFFFFFFzzz";

    private static (List<string> WhereClauses, List<object> Parameters) BuildSqliteWhere(
        Guid? userId, LlmSurface? surface, DateTimeOffset from, DateTimeOffset to)
    {
        // Reporting / status reads count only Committed rows so in-flight reservations (issue #1313)
        // never inflate usage summaries or the quota-status endpoint. Enforcement counts reservations,
        // but it does so inside TryReserveAsync's serialized transaction, not here.
        var clauses = new List<string> { "CreatedAt >= {0}", "CreatedAt < {1}", $"Status = {{2}}" };
        var parameters = new List<object>
        {
            from.ToString(SqliteDateFormat),
            to.ToString(SqliteDateFormat),
            StatusCommitted
        };
        var paramIndex = 3;

        if (userId.HasValue)
        {
            clauses.Add($"UserId = {{{paramIndex}}}");
            parameters.Add(userId.Value.ToString().ToUpperInvariant());
            paramIndex++;
        }

        if (surface.HasValue)
        {
            clauses.Add($"Surface = {{{paramIndex}}}");
            parameters.Add((int)surface.Value);
        }

        return (clauses, parameters);
    }

    private IQueryable<LlmUsageRecord> BuildFilteredQuery(
        Guid? userId,
        LlmSurface? surface,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        // Reporting reads count only Committed rows (see BuildSqliteWhere).
        var query = _dbSet.AsNoTracking()
            .Where(r => r.CreatedAt >= from && r.CreatedAt < to
                && r.Status == LlmUsageRecordStatus.Committed);

        if (userId.HasValue)
            query = query.Where(r => r.UserId == userId.Value);

        if (surface.HasValue)
            query = query.Where(r => r.Surface == surface.Value);

        return query;
    }
}
