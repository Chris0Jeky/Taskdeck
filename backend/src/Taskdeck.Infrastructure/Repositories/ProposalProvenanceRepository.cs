using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class ProposalProvenanceRepository : Repository<ProposalProvenance>, IProposalProvenanceRepository
{
    public ProposalProvenanceRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<ProposalProvenance?> GetByProposalIdAsync(
        Guid proposalId,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .AsNoTracking()
            .Include(pp => pp.Fields)
                .ThenInclude(f => f.EvidenceLinks)
            .FirstOrDefaultAsync(pp => pp.ProposalId == proposalId, cancellationToken);
    }
}
