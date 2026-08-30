using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ICaptureBackfillStore"/>. Shares the scoped
/// <see cref="TaskdeckDbContext"/> with <see cref="UnitOfWork"/> and <see cref="EfCaptureStore"/>,
/// so one backfill batch — its captures, their assets and the progress marker — commits in a single
/// <c>SaveChangesAsync</c>.
/// </summary>
public sealed class EfCaptureBackfillStore : ICaptureBackfillStore
{
    /// <summary>The shipped capture lane predicate, spelled exactly as <see cref="LlmQueueRepository"/> spells it.</summary>
    private const string CaptureRequestTypeLike = "inbox.capture.%";

    private readonly TaskdeckDbContext _context;

    public EfCaptureBackfillStore(TaskdeckDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Capture-shaped queue rows that are missing a capture OR have been written since their capture
    /// last was. The second half is what makes this a reconcile pass: an aggregate whose queue row
    /// moved on (an edit while dual-write was off, a durable write that failed and was swallowed)
    /// would otherwise stay stale forever and the read switch would serve it.
    /// </summary>
    private IQueryable<LlmRequest> Backlog =>
        _context.LlmRequests
            .AsNoTracking()
            // The lane predicate is the shipped capture predicate (inbox.capture.%), so transcript
            // captures — which nest under the same prefix — are backfilled too.
            .Where(request => EF.Functions.Like(request.RequestType, CaptureRequestTypeLike))
            .Where(request => !_context.Captures.Any(capture =>
                capture.Id == request.Id && capture.UpdatedAt >= request.UpdatedAt));

    public async Task<IReadOnlyList<LlmRequest>> GetLegacyCaptureBacklogAsync(
        int batchSize,
        IReadOnlyCollection<Guid> excludedIds,
        CancellationToken cancellationToken = default)
    {
        if (batchSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "Batch size must be at least 1");
        }

        ArgumentNullException.ThrowIfNull(excludedIds);
        var excluded = excludedIds as ISet<Guid> ?? excludedIds.ToHashSet();

        if (_context.Database.IsSqlite())
        {
            // SQLite cannot translate ORDER BY on a DateTimeOffset column from LINQ, so the ordering
            // and the bound live in raw SQL - the same treatment LlmQueueRepository.GetCapturesByUserAsync
            // gives the Inbox listing. The NOT EXISTS clause is the divergence join: a row leaves the
            // backlog only once a capture exists for it AND that capture is at least as fresh as the
            // queue row. Oldest first, so the backlog drains in intake order.
            // Rows this run has already failed on are excluded here rather than filtered afterwards,
            // so a poisoned head cannot consume the whole batch on every iteration.
            // FromSqlInterpolated parameterises every hole; the exclusion list is bounded by the
            // number of distinct failures in one run, so it is fetched generously and trimmed below.
            FormattableString sql =
                $"""
                SELECT * FROM LlmRequests
                WHERE RequestType LIKE {CaptureRequestTypeLike}
                  AND NOT EXISTS (
                        SELECT 1 FROM Captures
                        WHERE Captures.Id = LlmRequests.Id
                          AND Captures.UpdatedAt >= LlmRequests.UpdatedAt)
                ORDER BY CreatedAt, Id
                LIMIT {batchSize + excluded.Count}
                """;
            var rows = await _context.LlmRequests
                .FromSqlInterpolated(sql)
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            return rows
                .Where(request => !excluded.Contains(request.Id))
                .OrderBy(request => request.CreatedAt)
                .ThenBy(request => request.Id.ToString(), StringComparer.Ordinal)
                .Take(batchSize)
                .ToList();
        }

        var query = Backlog;
        if (excluded.Count > 0)
        {
            var excludedArray = excluded.ToArray();
            query = query.Where(request => !excludedArray.Contains(request.Id));
        }

        return await query
            .OrderBy(request => request.CreatedAt)
            .ThenBy(request => request.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountLegacyCaptureBacklogAsync(CancellationToken cancellationToken = default)
    {
        if (_context.Database.IsSqlite())
        {
            // Same reason the batch read drops to raw SQL: the SQLite provider cannot translate a
            // DateTimeOffset comparison, and the divergence join is built on one. Both stamps are
            // written as UTC (DateTimeOffset.UtcNow everywhere), so the stored TEXT form is directly
            // comparable - the same assumption the shipped ORDER BY CreatedAt in
            // LlmQueueRepository.GetCapturesByUserAsync already relies on.
            var counts = await _context.Database
                .SqlQuery<int>(
                    $"""
                    SELECT COUNT(*) AS "Value" FROM LlmRequests
                    WHERE RequestType LIKE {CaptureRequestTypeLike}
                      AND NOT EXISTS (
                            SELECT 1 FROM Captures
                            WHERE Captures.Id = LlmRequests.Id
                              AND Captures.UpdatedAt >= LlmRequests.UpdatedAt)
                    """)
                .ToListAsync(cancellationToken);
            return counts.Single();
        }

        return await Backlog.CountAsync(cancellationToken);
    }

    public Task ReleaseTrackedBatchAsync(CancellationToken cancellationToken = default)
    {
        _context.ChangeTracker.Clear();
        return Task.CompletedTask;
    }

    public Task<CaptureBackfillState?> GetStateAsync(string key, CancellationToken cancellationToken = default)
        => _context.CaptureBackfillStates
            .AsNoTracking()
            .FirstOrDefaultAsync(state => state.Key == key, cancellationToken);

    public async Task SaveStateAsync(CaptureBackfillState state, CancellationToken cancellationToken = default)
    {
        var tracked = await _context.CaptureBackfillStates
            .FirstOrDefaultAsync(existing => existing.Id == state.Id, cancellationToken);
        if (tracked is null)
        {
            await _context.CaptureBackfillStates.AddAsync(state, cancellationToken);
            return;
        }

        if (!ReferenceEquals(tracked, state))
        {
            _context.Entry(tracked).CurrentValues.SetValues(state);
        }
    }
}
