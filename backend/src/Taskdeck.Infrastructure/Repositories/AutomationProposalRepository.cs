using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class AutomationProposalRepository : Repository<AutomationProposal>, IAutomationProposalRepository
{
    private const int DefaultLimit = 100;
    private static readonly ProposalStatus[] ReviewedStatuses =
    [
        ProposalStatus.Approved,
        ProposalStatus.Rejected,
        ProposalStatus.Applied,
        ProposalStatus.Failed,
    ];

    public AutomationProposalRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public override async Task<AutomationProposal?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(p => p.Operations)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<int> CountPendingReviewByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return await _dbSet
            .AsNoTracking()
            .Where(proposal =>
                proposal.RequestedByUserId == userId &&
                proposal.Status == ProposalStatus.PendingReview)
            // Hide currently-snoozed pending proposals so the Today/Home badge matches
            // the visible review queue. Status is already gated to PendingReview above,
            // so only the snooze window needs checking here.
            .Where(proposal =>
                proposal.DeferredUntil == null ||
                proposal.DeferredUntil <= now)
            .CountAsync(cancellationToken);
    }

    public async Task<bool> HasReviewedByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .AsNoTracking()
            .AnyAsync(
                proposal => proposal.DecidedByUserId == userId && ReviewedStatuses.Contains(proposal.Status),
                cancellationToken);
    }

    public async Task<IEnumerable<AutomationProposal>> GetByStatusAsync(ProposalStatus status, int limit = 100, CancellationToken cancellationToken = default)
    {
        return await GetLimitedWithOperationsAsync(
            _dbSet.Where(p => p.Status == status),
            limit,
            includeDeferred: false,
            cancellationToken);
    }

    public async Task<IReadOnlyList<AutomationProposal>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var uniqueIds = ids
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (uniqueIds.Count == 0)
        {
            return Array.Empty<AutomationProposal>();
        }

        return await _dbSet
            .Where(proposal => uniqueIds.Contains(proposal.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<AutomationProposal>> GetByBoardIdAsync(Guid boardId, int limit = 100, CancellationToken cancellationToken = default)
    {
        return await GetLimitedWithOperationsAsync(
            _dbSet.Where(p => p.BoardId == boardId),
            limit,
            includeDeferred: false,
            cancellationToken);
    }

    public async Task<IEnumerable<AutomationProposal>> GetByUserIdAsync(Guid userId, int limit = 100, bool includeDeferred = false, CancellationToken cancellationToken = default)
    {
        return await GetLimitedWithOperationsAsync(
            _dbSet.Where(p => p.RequestedByUserId == userId),
            limit,
            includeDeferred,
            cancellationToken);
    }

    public async Task<IEnumerable<AutomationProposal>> GetByRiskLevelAsync(RiskLevel riskLevel, int limit = 100, CancellationToken cancellationToken = default)
    {
        return await GetLimitedWithOperationsAsync(
            _dbSet.Where(p => p.RiskLevel == riskLevel),
            limit,
            includeDeferred: false,
            cancellationToken);
    }

    public async Task<AutomationProposal?> GetBySourceReferenceAsync(ProposalSourceType sourceType, string referenceId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(p => p.Operations)
            .FirstOrDefaultAsync(p => p.SourceType == sourceType && p.SourceReferenceId == referenceId, cancellationToken);
    }

    public async Task<AutomationProposal?> GetByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(p => p.Operations)
            .FirstOrDefaultAsync(p => p.CorrelationId == correlationId, cancellationToken);
    }

    public async Task<AutomationProposal?> GetLatestByOperationTargetAsync(
        string targetType,
        string targetId,
        CancellationToken cancellationToken = default)
    {
        return await GetLatestByOperationTargetCoreAsync(
            targetType,
            targetId,
            actionType: null,
            sourceType: null,
            cancellationToken);
    }

    public async Task<AutomationProposal?> GetLatestByOperationTargetAsync(
        string targetType,
        string targetId,
        string actionType,
        ProposalSourceType sourceType,
        CancellationToken cancellationToken = default)
    {
        return await GetLatestByOperationTargetCoreAsync(
            targetType,
            targetId,
            actionType,
            sourceType,
            cancellationToken);
    }

    private async Task<AutomationProposal?> GetLatestByOperationTargetCoreAsync(
        string targetType,
        string targetId,
        string? actionType,
        ProposalSourceType? sourceType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(targetType) || string.IsNullOrWhiteSpace(targetId))
            return null;

        var matchingOperations = _context.AutomationProposalOperations
            .Where(operation => operation.TargetType == targetType && operation.TargetId == targetId);

        if (!string.IsNullOrWhiteSpace(actionType))
        {
            matchingOperations = matchingOperations.Where(operation => operation.ActionType == actionType);
        }

        var orderedOperations = (await matchingOperations
            .Select(operation => new
            {
                operation.ProposalId,
                operation.UpdatedAt,
            })
            .ToListAsync(cancellationToken))
            .OrderByDescending(operation => operation.UpdatedAt)
            .ToList();

        if (orderedOperations.Count == 0)
            return null;

        Guid proposalId;
        if (sourceType.HasValue)
        {
            var candidateProposalIds = orderedOperations
                .Select(operation => operation.ProposalId)
                .Distinct()
                .ToList();

            var matchingProposalIds = await _dbSet
                .Where(proposal => candidateProposalIds.Contains(proposal.Id) && proposal.SourceType == sourceType.Value)
                .Select(proposal => proposal.Id)
                .ToListAsync(cancellationToken);

            var matchingProposalIdSet = matchingProposalIds.ToHashSet();
            proposalId = orderedOperations
                .Select(operation => operation.ProposalId)
                .FirstOrDefault(id => matchingProposalIdSet.Contains(id));
        }
        else
        {
            proposalId = orderedOperations
                .Select(operation => operation.ProposalId)
                .FirstOrDefault();
        }

        if (proposalId == Guid.Empty)
            return null;

        return await _dbSet
            .Include(p => p.Operations)
            .FirstOrDefaultAsync(p => p.Id == proposalId, cancellationToken);
    }

    public async Task<IReadOnlyList<AutomationProposal>> GetPendingByOperationTargetAsync(
        string targetType,
        string targetId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetType) || string.IsNullOrWhiteSpace(targetId))
            return Array.Empty<AutomationProposal>();

        var normalizedTargetType = targetType.Trim().ToLowerInvariant();
        var targetIsGuid = Guid.TryParse(targetId, out var normalizedTargetGuid);
        var now = DateTime.UtcNow;

        var candidateOperations = await _context.AutomationProposalOperations
            .Join(
                _dbSet.Where(p => p.Status == ProposalStatus.PendingReview && p.ExpiresAt > now),
                operation => operation.ProposalId,
                proposal => proposal.Id,
                (operation, proposal) => new
                {
                    operation.ProposalId,
                    operation.TargetType,
                    operation.TargetId
                })
            .Where(op => op.TargetType.ToLower() == normalizedTargetType && op.TargetId != null)
            .ToListAsync(cancellationToken);

        var proposalIds = candidateOperations
            .Where(op => TargetIdMatches(op.TargetId!, targetId, targetIsGuid, normalizedTargetGuid))
            .Select(op => op.ProposalId)
            .Distinct()
            .ToList();

        if (proposalIds.Count == 0)
            return Array.Empty<AutomationProposal>();

        return await _dbSet
            .Include(p => p.Operations)
            .Where(p => proposalIds.Contains(p.Id) && p.Status == ProposalStatus.PendingReview)
            .ToListAsync(cancellationToken);
    }

    private static bool TargetIdMatches(string storedTargetId, string requestedTargetId, bool requestedTargetIsGuid, Guid requestedTargetGuid)
    {
        if (requestedTargetIsGuid && Guid.TryParse(storedTargetId, out var storedTargetGuid))
            return storedTargetGuid == requestedTargetGuid;

        return string.Equals(storedTargetId, requestedTargetId, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<IEnumerable<AutomationProposal>> GetExpiredAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return await _dbSet
            .Include(p => p.Operations)
            .Where(p => p.Status == ProposalStatus.PendingReview && p.ExpiresAt < now)
            .ToListAsync(cancellationToken);
    }

    private static readonly ProposalStatus[] TerminalStatuses =
    [
        ProposalStatus.Applied,
        ProposalStatus.Rejected,
    ];

    public async Task<IReadOnlyList<AutomationProposal>> GetTerminalByActionTypeAsync(
        string actionType,
        Guid? boardId,
        Guid userId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actionType))
            return Array.Empty<AutomationProposal>();

        var boundedLimit = limit <= 0 ? DefaultLimit : limit;

        // Find proposal IDs whose operations match the action type
        var matchingProposalIds = _context.AutomationProposalOperations
            .Where(op => op.ActionType == actionType)
            .Select(op => op.ProposalId)
            .Distinct();

        var query = _dbSet
            .Where(p => matchingProposalIds.Contains(p.Id))
            .Where(p => TerminalStatuses.Contains(p.Status));

        if (boardId.HasValue)
        {
            // Board-scoped: show all decisions for this board regardless of who created them,
            // so reviewers see the board's base rate for this action type.
            query = query.Where(p => p.BoardId == boardId.Value);
        }
        else
        {
            // User-scoped fallback: only show the caller's own decisions.
            query = query.Where(p => p.RequestedByUserId == userId);
        }

        var proposalIds = await query
            .AsNoTracking()
            .OrderByDescending(p => p.DecidedAt)
            .ThenByDescending(p => p.UpdatedAt)
            .Take(boundedLimit)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        if (proposalIds.Count == 0)
            return Array.Empty<AutomationProposal>();

        var proposals = await _dbSet
            .AsNoTracking()
            .Include(p => p.Operations)
            .Where(p => proposalIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        return proposals
            .OrderByDescending(p => p.DecidedAt)
            .ThenByDescending(p => p.UpdatedAt)
            .ToList();
    }

    private async Task<IReadOnlyList<AutomationProposal>> GetLimitedWithOperationsAsync(
        IQueryable<AutomationProposal> baseQuery,
        int limit,
        bool includeDeferred,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var boundedLimit = limit <= 0 ? DefaultLimit : limit;

        if (!includeDeferred)
        {
            // Hide snoozed PENDING proposals (DeferredUntil in the future) from review-queue
            // reads, but never hide a decided/terminal proposal that happens to retain a stale
            // snooze value. The status gate keeps Approved/Rejected/etc. visible regardless of
            // DeferredUntil. Completeness-sensitive callers (the GDPR data export) opt out with
            // includeDeferred:true so a snoozed proposal is never silently dropped.
            baseQuery = baseQuery.Where(p =>
                p.Status != ProposalStatus.PendingReview ||
                p.DeferredUntil == null ||
                p.DeferredUntil <= now);
        }

        var topProposalIds = await baseQuery
            .OrderByDescending(p => p.ExpiresAt)
            .Take(boundedLimit)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        if (topProposalIds.Count == 0)
            return Array.Empty<AutomationProposal>();

        var proposals = await _dbSet
            .Include(p => p.Operations)
            .Where(p => topProposalIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        return proposals
            .OrderByDescending(p => p.CreatedAt)
            .ToList();
    }
}
