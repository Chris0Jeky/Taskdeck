using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class ProposalFeedbackRepository : Repository<ProposalFeedback>, IProposalFeedbackRepository
{
    private const int MaxLimit = 1000;

    public ProposalFeedbackRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<ProposalFeedback?> GetByProposalAndUserAsync(Guid proposalId, Guid userId, CancellationToken cancellationToken = default)
    {
        // Tracked on purpose: the report flow may refine this row's reason in place
        // (the first specific reason wins) and persist it through the shared UnitOfWork.
        return await _dbSet
            .FirstOrDefaultAsync(f => f.ProposalId == proposalId && f.ReportedByUserId == userId, cancellationToken);
    }

    public async Task<IReadOnlyList<ProposalFeedback>> GetAllByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // Order before the cap so the bounded sample is deterministic (newest-first); the
        // (ReportedByUserId, CreatedAt) index covers this. SQLite's EF provider can't ORDER BY a
        // DateTimeOffset column in LINQ, so the order + LIMIT live in raw SQL there (no Includes,
        // so the ORDER BY survives; the in-memory re-sort is a cheap belt-and-suspenders).
        if (_context.Database.IsSqlite())
        {
            var rows = await _dbSet
                .FromSqlInterpolated($"SELECT * FROM ProposalFeedbacks WHERE ReportedByUserId = {userId} ORDER BY CreatedAt DESC LIMIT {MaxLimit}")
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            return rows.OrderByDescending(f => f.CreatedAt).ToList();
        }

        return await _dbSet
            .AsNoTracking()
            .Where(f => f.ReportedByUserId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .Take(MaxLimit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProposalFeedback>> GetAllByUserIdForExportAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // Data-portability export: the COMPLETE user-scoped set, deliberately uncapped (the cohort
        // read's 1000-row cap would silently truncate a heavy reporter's export). No SQL ORDER BY:
        // SQLite's EF provider can't ORDER BY a DateTimeOffset column in LINQ, and the export
        // materializes the whole set anyway, so we sort newest-first in memory and avoid the
        // raw-SQL path entirely. The (ReportedByUserId) filter is index-covered.
        var rows = await _dbSet
            .AsNoTracking()
            .Where(f => f.ReportedByUserId == userId)
            .ToListAsync(cancellationToken);

        return rows.OrderByDescending(f => f.CreatedAt).ToList();
    }
}
