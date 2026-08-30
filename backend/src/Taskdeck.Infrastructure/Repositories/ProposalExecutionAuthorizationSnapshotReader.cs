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
    private readonly DevelopmentSandboxSettings _sandboxSettings;

    public ProposalExecutionAuthorizationSnapshotReader(
        TaskdeckDbContext context,
        DevelopmentSandboxSettings? sandboxSettings = null)
    {
        _context = context;
        _sandboxSettings = sandboxSettings ?? new DevelopmentSandboxSettings();
    }

    public async Task<ProposalExecutionAuthorizationSnapshot?> FindAsync(
        Guid proposalId,
        Guid callerUserId,
        CancellationToken cancellationToken = default)
    {
        var proposals = _context.AutomationProposals
            .AsNoTracking()
            .Where(proposal => proposal.Id == proposalId);

        if (!_sandboxSettings.Enabled)
        {
            proposals = proposals.Where(proposal =>
                (!proposal.BoardId.HasValue && proposal.RequestedByUserId == callerUserId) ||
                (proposal.BoardId.HasValue &&
                    (_context.Boards.Any(board =>
                        board.Id == proposal.BoardId.Value &&
                        board.OwnerId == callerUserId) ||
                     _context.BoardAccesses.Any(access =>
                        access.BoardId == proposal.BoardId.Value &&
                        access.UserId == callerUserId))));
        }

        return await proposals
            .Select(proposal => new ProposalExecutionAuthorizationSnapshot(
                proposal.Id,
                proposal.BoardId,
                proposal.RequestedByUserId,
                proposal.ApprovedRevisionId))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
