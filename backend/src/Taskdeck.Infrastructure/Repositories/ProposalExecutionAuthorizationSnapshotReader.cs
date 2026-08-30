using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Services;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

/// <summary>
/// EF projection for batch execute's phase-one authorization read. <c>AsNoTracking</c>
/// is load-bearing: the request-scoped executor must query persisted proposal status again when
/// each item reaches its turn.
/// </summary>
public sealed class ProposalExecutionAuthorizationSnapshotReader
    : IProposalExecutionAuthorizationSnapshotReader
{
    private readonly TaskdeckDbContext _context;

    public ProposalExecutionAuthorizationSnapshotReader(TaskdeckDbContext context)
    {
        _context = context;
    }

    public async Task<ProposalExecutionAuthorizationSnapshot?> FindAsync(
        Guid proposalId,
        CancellationToken cancellationToken = default)
    {
        return await _context.AutomationProposals
            .AsNoTracking()
            .Where(proposal => proposal.Id == proposalId)
            .Select(proposal => new ProposalExecutionAuthorizationSnapshot(
                proposal.Id,
                proposal.BoardId,
                proposal.RequestedByUserId,
                proposal.ApprovedRevisionId))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
