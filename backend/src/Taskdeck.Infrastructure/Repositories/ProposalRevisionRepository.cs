using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class ProposalRevisionRepository : Repository<ProposalRevision>, IProposalRevisionRepository
{
    /// <summary>
    /// Number of ids sent per batch read, for both the ref projection and the by-id payload load.
    /// <para>
    /// The id count reaching these methods is not bounded by this layer — <c>ProposalFilterDto.Limit</c>
    /// has no upper bound in the Application contract, and the API controller's own 500-item clamp
    /// binds only the HTTP callers. Chunking keeps the work per statement bounded and predictable
    /// regardless of how large a page a caller asks for.
    /// </para>
    /// <para>
    /// NOT justified by SQLite's bound-parameter cap: on EF Core 8 + SQLite a <c>Contains</c> over a
    /// captured list is translated as a single collection parameter (<c>json_each</c>), so the
    /// parameter cap does not bind here (#1444 review corrected an earlier comment that claimed it
    /// did). The chunk size is a work-per-statement bound, and the exact query count for a given id
    /// count is an implementation detail, not a contract.
    /// </para>
    /// <para>
    /// Deliberately chunks rather than following <c>ArtefactExtractionRepository</c>'s cap-and-throw
    /// convention for batch reads: there the caller supplies the id set explicitly, so an oversized
    /// set is a caller contract violation, whereas here the ids are derived from a page of proposals
    /// the service just read. Throwing would turn a large-but-legal list request into a 500 on a read
    /// path, so this degrades into a few queries instead.
    /// </para>
    /// </summary>
    private const int IdChunkSize = 200;

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

    public async Task<IReadOnlyList<ProposalRevisionRef>> GetRefsByProposalIdsAsync(
        IEnumerable<Guid> proposalIds,
        CancellationToken cancellationToken = default)
    {
        // Distinct so a caller passing repeated ids cannot inflate the chunk count or duplicate rows.
        var ids = proposalIds.Distinct().ToList();
        if (ids.Count == 0)
            return Array.Empty<ProposalRevisionRef>();

        var results = new List<ProposalRevisionRef>();
        for (var offset = 0; offset < ids.Count; offset += IdChunkSize)
        {
            var chunk = ids.GetRange(offset, Math.Min(IdChunkSize, ids.Count - offset));

            // Projects to the four columns the effective-revision rules compare. RevisedPayload is
            // deliberately NOT selected: it is unbounded, and the selector never reads it (#1444).
            var chunkRefs = await _dbSet
                .AsNoTracking()
                .Where(r => chunk.Contains(r.ProposalId))
                .OrderBy(r => r.ProposalId)
                .ThenBy(r => r.RevisionNumber)
                .Select(r => new ProposalRevisionRef(r.Id, r.ProposalId, r.RevisionNumber, r.RevisedAt))
                .ToListAsync(cancellationToken);

            results.AddRange(chunkRefs);
        }

        return results;
    }

    public async Task<IReadOnlyList<ProposalRevision>> GetByIdsAsync(
        IEnumerable<Guid> revisionIds,
        CancellationToken cancellationToken = default)
    {
        var ids = revisionIds.Distinct().ToList();
        if (ids.Count == 0)
            return Array.Empty<ProposalRevision>();

        var results = new List<ProposalRevision>();
        for (var offset = 0; offset < ids.Count; offset += IdChunkSize)
        {
            var chunk = ids.GetRange(offset, Math.Min(IdChunkSize, ids.Count - offset));

            var chunkRevisions = await _dbSet
                .AsNoTracking()
                .Where(r => chunk.Contains(r.Id))
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
