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
        // (last-specific-wins) and persist it through the shared UnitOfWork.
        return await _dbSet
            .FirstOrDefaultAsync(f => f.ProposalId == proposalId && f.ReportedByUserId == userId, cancellationToken);
    }

    public async Task<IReadOnlyList<ProposalFeedback>> GetAllByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // Order before the cap so the bounded sample is deterministic (newest-first), not an
        // arbitrary slice -- the (ReportedByUserId, CreatedAt) index covers this.
        return await _dbSet
            .AsNoTracking()
            .Where(f => f.ReportedByUserId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .Take(MaxLimit)
            .ToListAsync(cancellationToken);
    }
}
