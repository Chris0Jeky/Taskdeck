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
}
