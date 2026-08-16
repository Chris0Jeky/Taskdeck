using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class ProposalProvenanceRepository : Repository<ProposalProvenance>, IProposalProvenanceRepository
{
    private const int SourceIdBatchSize = 400;

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

    public async Task<int> DeleteEvidenceLinksBySourceIdsAsync(
        string sourceType,
        IReadOnlyCollection<Guid> sourceIds,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceType) || sourceIds.Count == 0)
        {
            return 0;
        }

        var sourceIdStrings = sourceIds
            .Where(id => id != Guid.Empty)
            .Select(id => id.ToString("D"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var deleted = 0;
        foreach (var batch in sourceIdStrings.Chunk(SourceIdBatchSize))
        {
            deleted += await _context.Set<ProvenanceEvidenceLink>()
                .Where(link => link.SourceType == sourceType && batch.Contains(link.SourceId))
                .ExecuteDeleteAsync(cancellationToken);
        }

        return deleted;
    }
}
