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

    // SQLite cannot translate DateTimeOffset comparisons from LINQ; use client-side filtering.
    private async Task<long> GetRequestCountSqliteAsync(
        Guid? userId, LlmSurface? surface, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var all = await _dbSet.AsNoTracking().ToListAsync(ct);
        return FilterClientSide(all, userId, surface, from, to).LongCount();
    }

    private async Task<long> GetTotalTokensSqliteAsync(
        Guid? userId, LlmSurface? surface, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var all = await _dbSet.AsNoTracking().ToListAsync(ct);
        var filtered = FilterClientSide(all, userId, surface, from, to);
        return filtered.Sum(r => (long)r.InputTokens + r.OutputTokens);
    }

    private async Task<(long, long, long)> GetUsageSummarySqliteAsync(
        Guid? userId, LlmSurface? surface, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var all = await _dbSet.AsNoTracking().ToListAsync(ct);
        var filtered = FilterClientSide(all, userId, surface, from, to).ToList();

        if (filtered.Count == 0)
            return (0, 0, 0);

        return (
            filtered.Sum(r => (long)r.InputTokens),
            filtered.Sum(r => (long)r.OutputTokens),
            filtered.Count);
    }

    private static IEnumerable<LlmUsageRecord> FilterClientSide(
        IEnumerable<LlmUsageRecord> records,
        Guid? userId,
        LlmSurface? surface,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var query = records.Where(r => r.CreatedAt >= from && r.CreatedAt < to);

        if (userId.HasValue)
            query = query.Where(r => r.UserId == userId.Value);

        if (surface.HasValue)
            query = query.Where(r => r.Surface == surface.Value);

        return query;
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
