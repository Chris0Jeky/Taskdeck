using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class ProposalRevisionRepository : Repository<ProposalRevision>, IProposalRevisionRepository
{
    /// <summary>
    /// Number of proposal ids sent per batch read. SQLite caps bound parameters per statement, and
    /// the page size reaching <see cref="GetByProposalIdsAsync"/> is not bounded by this layer —
    /// <c>ProposalFilterDto.Limit</c> has no upper bound in the Application contract, and the API
    /// controller's own 500-item clamp binds only the HTTP callers. Reading in fixed chunks keeps a
    /// large caller-supplied page to a few queries instead of one that could exceed the parameter
    /// cap, and stays O(chunks) rather than the O(proposals) N+1 this method exists to remove.
    /// <para>
    /// Deliberately chunks rather than following <c>ArtefactExtractionRepository</c>'s cap-and-throw
    /// convention for batch reads: there the caller supplies the id set explicitly, so an oversized
    /// set is a caller contract violation, whereas here the ids are derived from a page of proposals
    /// the service just read. Throwing would turn a large-but-legal list request into a 500 on a read
    /// path, so this degrades into a few queries instead.
    /// </para>
    /// </summary>
    private const int ProposalIdChunkSize = 200;

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

    public async Task<IReadOnlyList<ProposalRevision>> GetByProposalIdsAsync(
        IEnumerable<Guid> proposalIds,
        CancellationToken cancellationToken = default)
    {
        // Distinct so a caller passing repeated ids cannot inflate the chunk count or duplicate rows.
        var ids = proposalIds.Distinct().ToList();
        if (ids.Count == 0)
            return Array.Empty<ProposalRevision>();

        var results = new List<ProposalRevision>();
        for (var offset = 0; offset < ids.Count; offset += ProposalIdChunkSize)
        {
            var chunk = ids.GetRange(offset, Math.Min(ProposalIdChunkSize, ids.Count - offset));

            var chunkRevisions = await _dbSet
                .AsNoTracking()
                .Where(r => chunk.Contains(r.ProposalId))
                .OrderBy(r => r.ProposalId)
                .ThenBy(r => r.RevisionNumber)
                .ToListAsync(cancellationToken);

            results.AddRange(chunkRevisions);
        }

        return results;
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
