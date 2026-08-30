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

    private IQueryable<LlmRequest> Backlog =>
        _context.LlmRequests
            .AsNoTracking()
            // The lane predicate is the shipped capture predicate (inbox.capture.%), so transcript
            // captures — which nest under the same prefix — are backfilled too.
            .Where(request => EF.Functions.Like(request.RequestType, CaptureRequestTypeLike))
            .Where(request => !_context.Captures.Any(capture => capture.Id == request.Id));

    public async Task<IReadOnlyList<LlmRequest>> GetLegacyCaptureBacklogAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (batchSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "Batch size must be at least 1");
        }

        if (_context.Database.IsSqlite())
        {
            // SQLite cannot translate ORDER BY on a DateTimeOffset column from LINQ, so the ordering
            // and the bound live in raw SQL - the same treatment LlmQueueRepository.GetCapturesByUserAsync
            // gives the Inbox listing. The NOT EXISTS clause is the anti-join that makes the backfill
            // idempotent and resumable: a row leaves the backlog the moment its capture is committed.
            // Oldest first, so the backlog drains in intake order and a partial run leaves the newest
            // rows outstanding. FromSqlInterpolated parameterises every hole.
            FormattableString sql =
                $"""
                SELECT * FROM LlmRequests
                WHERE RequestType LIKE {CaptureRequestTypeLike}
                  AND NOT EXISTS (SELECT 1 FROM Captures WHERE Captures.Id = LlmRequests.Id)
                ORDER BY CreatedAt, Id
                LIMIT {batchSize}
                """;
            var rows = await _context.LlmRequests
                .FromSqlInterpolated(sql)
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            return rows
                .OrderBy(request => request.CreatedAt)
                .ThenBy(request => request.Id.ToString(), StringComparer.Ordinal)
                .ToList();
        }

        return await Backlog
            .OrderBy(request => request.CreatedAt)
            .ThenBy(request => request.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountLegacyCaptureBacklogAsync(CancellationToken cancellationToken = default)
        => Backlog.CountAsync(cancellationToken);

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
