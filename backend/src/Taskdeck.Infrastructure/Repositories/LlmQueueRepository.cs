using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

/// <summary>
/// SCAFFOLDING: Placeholder repository implementation for LlmRequest entity.
/// </summary>
public class LlmQueueRepository : Repository<LlmRequest>, ILlmQueueRepository
{
    private const string CaptureRequestTypeLike = "inbox.capture.%";

    public LlmQueueRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<LlmRequest>> GetPendingAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        if (_context.Database.IsSqlite())
        {
            return await _context.LlmRequests
                .FromSqlInterpolated(
                    $"SELECT * FROM LlmRequests WHERE Status = {(int)RequestStatus.Pending} ORDER BY CreatedAt ASC LIMIT {limit}")
                .Include(lr => lr.User)
                .Include(lr => lr.Board)
                .ToListAsync(cancellationToken);
        }

        return await _context.LlmRequests
            .Include(lr => lr.User)
            .Include(lr => lr.Board)
            .Where(lr => lr.Status == RequestStatus.Pending)
            .OrderBy(lr => lr.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<(int TotalCaptures, int NewCount, int FailedCount, int TriagingCount, int TriagedCount)> GetCaptureSummaryByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var captureQuery = _context.LlmRequests
            .AsNoTracking()
            .Where(request =>
                request.UserId == userId
                && EF.Functions.Like(request.RequestType, CaptureRequestTypeLike));

        var statusCounts = await captureQuery
            .GroupBy(request => request.Status)
            .Select(group => new
            {
                Status = group.Key,
                Count = group.Count(),
            })
            .ToListAsync(cancellationToken);

        var countsByStatus = statusCounts.ToDictionary(group => group.Status, group => group.Count);
        var completedWithLinkedProposal = await CountCompletedCapturesWithProposalAsync(
            userId,
            cancellationToken);

        countsByStatus.TryGetValue(RequestStatus.Completed, out var completedCount);
        countsByStatus.TryGetValue(RequestStatus.Pending, out var pendingCount);
        countsByStatus.TryGetValue(RequestStatus.Failed, out var failedCount);
        countsByStatus.TryGetValue(RequestStatus.Processing, out var processingCount);

        return (
            TotalCaptures: countsByStatus.Values.Sum(),
            NewCount: pendingCount,
            FailedCount: failedCount,
            TriagingCount: processingCount,
            TriagedCount: Math.Max(0, completedCount - completedWithLinkedProposal));
    }

    private async Task<int> CountCompletedCapturesWithProposalAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (_context.Database.IsSqlite())
        {
            return await _context.Database
                .SqlQuery<int>(
                    $"""
                    SELECT COUNT(*) AS Value
                    FROM LlmRequests
                    WHERE UserId = {userId}
                      AND RequestType LIKE {CaptureRequestTypeLike}
                      AND Status = {(int)RequestStatus.Completed}
                      AND json_extract(Payload, '$.provenance.proposalId') IS NOT NULL
                    """)
                .SingleAsync(cancellationToken);
        }

        return await _context.LlmRequests
            .AsNoTracking()
            .Where(request =>
                request.UserId == userId
                && request.Status == RequestStatus.Completed
                && EF.Functions.Like(request.RequestType, CaptureRequestTypeLike)
                && request.Payload.Contains("\"proposalId\":\""))
            .CountAsync(cancellationToken);
    }

    public async Task<IEnumerable<LlmRequest>> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (_context.Database.IsSqlite())
        {
            // FromSqlInterpolated + Include may not preserve ORDER BY from the raw SQL
            // (EF Core wraps it in a subquery), so sort in-memory after materialization.
            var results = await _context.LlmRequests
                .FromSqlInterpolated($"SELECT * FROM LlmRequests WHERE UserId = {userId}")
                .Include(lr => lr.Board)
                .ToListAsync(cancellationToken);
            return results.OrderByDescending(lr => lr.CreatedAt).ToList();
        }

        return await _context.LlmRequests
            .Include(lr => lr.Board)
            .Where(lr => lr.UserId == userId)
            .OrderByDescending(lr => lr.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<LlmRequest>> GetCapturesByUserAsync(Guid userId, int limit, int offset, CancellationToken cancellationToken = default)
    {
        if (limit < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Limit must be at least 1.");
        }

        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "Offset cannot be negative.");
        }

        if (_context.Database.IsSqlite())
        {
            // SQLite cannot translate ORDER BY on a DateTimeOffset column from LINQ, so the ordering +
            // LIMIT/OFFSET live in raw SQL. The (CreatedAt desc, Id) total order keeps paging stable so no
            // row is skipped or duplicated across pages. The re-sort defensively re-establishes that order
            // in case EF reshapes the query; the tie-break uses Id.ToString() with an ordinal comparison so
            // it matches SQLite's TEXT comparison of the stored Guid exactly (the raw SQL's `ORDER BY Id`),
            // rather than Guid.CompareTo, which orders by a different key. No Include: the only caller
            // (CaptureService.ListAsync) reads the scalar BoardId, never the Board navigation.
            var rows = await _context.LlmRequests
                .FromSqlInterpolated(
                    $"SELECT * FROM LlmRequests WHERE UserId = {userId} AND RequestType LIKE {CaptureRequestTypeLike} ORDER BY CreatedAt DESC, Id LIMIT {limit} OFFSET {offset}")
                .ToListAsync(cancellationToken);
            return rows
                .OrderByDescending(lr => lr.CreatedAt)
                .ThenBy(lr => lr.Id.ToString(), StringComparer.Ordinal)
                .ToList();
        }

        // Non-SQLite providers (e.g. the Postgres Testcontainer path) order in the database. EF.Functions.Like
        // mirrors the capture predicate used across this repository (e.g. GetCaptureSummaryByUserAsync); on a
        // case-sensitive provider it is stricter than IsCaptureRequestType, but production is SQLite-only.
        return await _context.LlmRequests
            .Where(lr => lr.UserId == userId && EF.Functions.Like(lr.RequestType, CaptureRequestTypeLike))
            .OrderByDescending(lr => lr.CreatedAt)
            .ThenBy(lr => lr.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<LlmRequest>> GetByStatusAsync(RequestStatus status, CancellationToken cancellationToken = default)
    {
        if (_context.Database.IsSqlite())
        {
            return await _context.LlmRequests
                .FromSqlInterpolated($"SELECT * FROM LlmRequests WHERE Status = {(int)status} ORDER BY CreatedAt DESC")
                .Include(lr => lr.User)
                .Include(lr => lr.Board)
                .ToListAsync(cancellationToken);
        }

        return await _context.LlmRequests
            .Include(lr => lr.User)
            .Include(lr => lr.Board)
            .Where(lr => lr.Status == status)
            .OrderByDescending(lr => lr.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public Task<IEnumerable<LlmRequest>> GetOldestPendingNonCaptureAsync(int limit, CancellationToken cancellationToken = default)
        => GetOldestByStatusAndCaptureKindAsync(RequestStatus.Pending, capture: false, limit, cancellationToken);

    public Task<IEnumerable<LlmRequest>> GetOldestProcessingCaptureAsync(int limit, CancellationToken cancellationToken = default)
        => GetOldestByStatusAndCaptureKindAsync(RequestStatus.Processing, capture: true, limit, cancellationToken);

    private async Task<IEnumerable<LlmRequest>> GetOldestByStatusAndCaptureKindAsync(
        RequestStatus status,
        bool capture,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Limit must be at least 1.");
        }

        if (_context.Database.IsSqlite())
        {
            // SQLite's EF provider cannot translate ORDER BY on a DateTimeOffset column, so the order +
            // LIMIT live in raw SQL. FromSqlInterpolated + Include wraps this in a subquery that does not
            // guarantee the raw ORDER BY survives to the final result (see GetByUserAsync); the inner LIMIT
            // still selects the correct oldest-N rows, so re-sort in memory to make oldest-first a contract.
            var rawQuery = capture
                ? _context.LlmRequests.FromSqlInterpolated(
                    $"SELECT * FROM LlmRequests WHERE Status = {(int)status} AND RequestType LIKE {CaptureRequestTypeLike} ORDER BY CreatedAt ASC LIMIT {limit}")
                : _context.LlmRequests.FromSqlInterpolated(
                    $"SELECT * FROM LlmRequests WHERE Status = {(int)status} AND RequestType NOT LIKE {CaptureRequestTypeLike} ORDER BY CreatedAt ASC LIMIT {limit}");

            var rows = await rawQuery
                .Include(lr => lr.User)
                .Include(lr => lr.Board)
                .ToListAsync(cancellationToken);
            return rows.OrderBy(lr => lr.CreatedAt).ToList();
        }

        // Non-SQLite providers (e.g. the Postgres Testcontainer path) translate the predicate + OrderBy +
        // Take into a single bounded query with guaranteed ordering.
        var query = _context.LlmRequests
            .Include(lr => lr.User)
            .Include(lr => lr.Board)
            .Where(lr => lr.Status == status);

        query = capture
            ? query.Where(lr => EF.Functions.Like(lr.RequestType, CaptureRequestTypeLike))
            : query.Where(lr => !EF.Functions.Like(lr.RequestType, CaptureRequestTypeLike));

        return await query
            .OrderBy(lr => lr.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountPendingNonCaptureAsync(CancellationToken cancellationToken = default)
        => CountByStatusAndCaptureKindAsync(RequestStatus.Pending, capture: false, cancellationToken);

    public Task<int> CountProcessingCaptureAsync(CancellationToken cancellationToken = default)
        => CountByStatusAndCaptureKindAsync(RequestStatus.Processing, capture: true, cancellationToken);

    private async Task<int> CountByStatusAndCaptureKindAsync(
        RequestStatus status,
        bool capture,
        CancellationToken cancellationToken)
    {
        var query = _context.LlmRequests
            .AsNoTracking()
            .Where(lr => lr.Status == status);

        query = capture
            ? query.Where(lr => EF.Functions.Like(lr.RequestType, CaptureRequestTypeLike))
            : query.Where(lr => !EF.Functions.Like(lr.RequestType, CaptureRequestTypeLike));

        return await query.CountAsync(cancellationToken);
    }

    public async Task<IEnumerable<LlmRequest>> GetByUserAndStatusAsync(Guid userId, RequestStatus status, CancellationToken cancellationToken = default)
    {
        if (_context.Database.IsSqlite())
        {
            return await _context.LlmRequests
                .FromSqlInterpolated($"SELECT * FROM LlmRequests WHERE UserId = {userId} AND Status = {(int)status} ORDER BY CreatedAt DESC")
                .AsNoTracking()
                .Include(lr => lr.User)
                .Include(lr => lr.Board)
                .ToListAsync(cancellationToken);
        }

        return await _context.LlmRequests
            .AsNoTracking()
            .Include(lr => lr.User)
            .Include(lr => lr.Board)
            .Where(lr => lr.UserId == userId && lr.Status == status)
            .OrderByDescending(lr => lr.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<Dictionary<RequestStatus, int>> GetStatusCountsByUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.LlmRequests
            .Where(r => r.UserId == userId)
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Count, cancellationToken);
    }

    public async Task<LlmRequest?> GetNextPendingAsync(CancellationToken cancellationToken = default)
    {
        if (_context.Database.IsSqlite())
        {
            return await _context.LlmRequests
                .FromSqlInterpolated(
                    $"SELECT * FROM LlmRequests WHERE Status = {(int)RequestStatus.Pending} ORDER BY CreatedAt ASC LIMIT 1")
                .Include(lr => lr.User)
                .Include(lr => lr.Board)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return await _context.LlmRequests
            .Include(lr => lr.User)
            .Include(lr => lr.Board)
            .Where(lr => lr.Status == RequestStatus.Pending)
            .OrderBy(lr => lr.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> TryClaimProcessingCaptureAsync(
        Guid requestId,
        DateTimeOffset expectedUpdatedAt,
        CancellationToken cancellationToken = default)
    {
        var claimedAt = DateTimeOffset.UtcNow;
        var rowsAffected = await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE LlmRequests
            SET UpdatedAt = {claimedAt}
            WHERE Id = {requestId}
              AND Status = {(int)RequestStatus.Processing}
              AND UpdatedAt = {expectedUpdatedAt}
              AND RequestType LIKE {CaptureRequestTypeLike}
            """,
            cancellationToken);

        return rowsAffected > 0;
    }

    public async Task<bool> TryClaimProcessingAsync(
        Guid requestId,
        DateTimeOffset expectedUpdatedAt,
        CancellationToken cancellationToken = default)
    {
        var claimedAt = DateTimeOffset.UtcNow;
        var rowsAffected = await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE LlmRequests
            SET Status = {(int)RequestStatus.Processing}, UpdatedAt = {claimedAt}
            WHERE Id = {requestId}
              AND Status = {(int)RequestStatus.Pending}
              AND UpdatedAt = {expectedUpdatedAt}
              AND RequestType NOT LIKE {CaptureRequestTypeLike}
            """,
            cancellationToken);

        if (rowsAffected == 0)
        {
            return false;
        }

        // The raw-SQL UPDATE bypasses the EF change tracker. If this context already
        // tracks the entity (e.g. it was materialized by GetByStatusAsync), reload it so
        // callers holding the instance -- and GetByIdAsync, whose FindAsync serves the
        // identity map -- observe the claimed Processing state instead of stale Pending.
        var tracked = _context.LlmRequests.Local.FirstOrDefault(lr => lr.Id == requestId);
        if (tracked != null)
        {
            await _context.Entry(tracked).ReloadAsync(cancellationToken);
        }

        return true;
    }
}
