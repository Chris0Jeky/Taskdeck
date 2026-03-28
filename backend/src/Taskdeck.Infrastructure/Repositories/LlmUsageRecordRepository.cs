using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class LlmUsageRecordRepository : Repository<LlmUsageRecord>, ILlmUsageRecordRepository
{
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

    private static (List<string> WhereClauses, List<object> Parameters) BuildSqliteWhere(
        Guid? userId, LlmSurface? surface, DateTimeOffset from, DateTimeOffset to)
    {
        var clauses = new List<string> { "CreatedAt >= {0}", "CreatedAt < {1}" };
        var parameters = new List<object> { from.ToString("o"), to.ToString("o") };
        var paramIndex = 2;

        if (userId.HasValue)
        {
            clauses.Add($"UserId = {{{paramIndex}}}");
            parameters.Add(userId.Value.ToString());
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
        var query = _dbSet.AsNoTracking()
            .Where(r => r.CreatedAt >= from && r.CreatedAt < to);

        if (userId.HasValue)
            query = query.Where(r => r.UserId == userId.Value);

        if (surface.HasValue)
            query = query.Where(r => r.Surface == surface.Value);

        return query;
    }
}
