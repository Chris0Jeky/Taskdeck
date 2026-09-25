using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class ProposalOutcomeRepository : Repository<ProposalOutcome>, IProposalOutcomeRepository
{
    public ProposalOutcomeRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<ProposalOutcome?> GetByProposalIdAsync(Guid proposalId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.ProposalId == proposalId, cancellationToken);
    }

    private const int MaxLimit = 1000;

    public async Task<IReadOnlyList<ProposalOutcome>> GetByUserIdAsync(Guid userId, int limit = 100, CancellationToken cancellationToken = default)
    {
        var boundedLimit = limit <= 0 ? 100 : Math.Min(limit, MaxLimit);

        return await _dbSet
            .AsNoTracking()
            .Where(o => o.DecidedByUserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .Take(boundedLimit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProposalOutcome>> GetByDecisionAsync(OutcomeDecision decision, int limit = 100, CancellationToken cancellationToken = default)
    {
        var boundedLimit = limit <= 0 ? 100 : Math.Min(limit, MaxLimit);

        return await _dbSet
            .AsNoTracking()
            .Where(o => o.Decision == decision)
            .OrderByDescending(o => o.CreatedAt)
            .Take(boundedLimit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProposalOutcome>> GetAllByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // Order by decision time before the cap so recent decisions survive to the
        // InsightsService cohort filter even when proposal creation order differs. SQLite's EF provider can't
        // ORDER BY a DateTimeOffset column in LINQ, so the order + LIMIT live in raw SQL there
        // (no Includes, so the ORDER BY survives; the in-memory re-sort is a cheap belt-and-suspenders).
        if (_context.Database.IsSqlite())
        {
            var rows = await _dbSet
                .FromSqlInterpolated($"SELECT * FROM ProposalOutcomes WHERE DecidedByUserId = {userId} ORDER BY DecidedAt DESC LIMIT {MaxLimit}")
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            return rows.OrderByDescending(o => o.DecidedAt).ToList();
        }

        return await _dbSet
            .AsNoTracking()
            .Where(o => o.DecidedByUserId == userId)
            .OrderByDescending(o => o.DecidedAt)
            .Take(MaxLimit)
            .ToListAsync(cancellationToken);
    }
}
