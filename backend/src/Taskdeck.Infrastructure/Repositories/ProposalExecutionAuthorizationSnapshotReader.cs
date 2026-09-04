using Microsoft.EntityFrameworkCore;
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
        Guid callerUserId,
        CancellationToken cancellationToken = default)
    {
        var proposals = _context.AutomationProposals
            .AsNoTracking()
            .Where(proposal => proposal.Id == proposalId);

        // The development sandbox never widens this scope (ADR-0068 / #1866): the execute path is
        // a write lane whose API-side and policy-engine gates are membership-backed in every
        // environment, so a sandbox-only widening here diverged from both.
        proposals = proposals.Where(proposal =>
            (!proposal.BoardId.HasValue && proposal.RequestedByUserId == callerUserId) ||
            (proposal.BoardId.HasValue &&
                (_context.Boards.Any(board =>
                    board.Id == proposal.BoardId.Value &&
                    board.OwnerId == callerUserId) ||
                 _context.BoardAccesses.Any(access =>
                    access.BoardId == proposal.BoardId.Value &&
                    access.UserId == callerUserId))));

        return await proposals
            .Select(proposal => new ProposalExecutionAuthorizationSnapshot(
                proposal.Id,
                proposal.BoardId,
                proposal.RequestedByUserId,
                proposal.ApprovedRevisionId))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
