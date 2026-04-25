using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class ProposalRevisionRepository : Repository<ProposalRevision>, IProposalRevisionRepository
{
    public ProposalRevisionRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<ProposalRevision>> GetByProposalIdAsync(
        Guid proposalId,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .AsNoTracking()
            .Where(r => r.ProposalId == proposalId)
            .OrderBy(r => r.RevisionNumber)
            .ToListAsync(cancellationToken);
    }

    public async Task<ProposalRevision?> GetLatestByProposalIdAsync(
        Guid proposalId,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .AsNoTracking()
            .Where(r => r.ProposalId == proposalId)
            .OrderByDescending(r => r.RevisionNumber)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<int> GetNextRevisionNumberAsync(
        Guid proposalId,
        CancellationToken cancellationToken = default)
    {
        var maxRevision = await _dbSet
            .Where(r => r.ProposalId == proposalId)
            .MaxAsync(r => (int?)r.RevisionNumber, cancellationToken);

        return (maxRevision ?? 0) + 1;
    }
}
