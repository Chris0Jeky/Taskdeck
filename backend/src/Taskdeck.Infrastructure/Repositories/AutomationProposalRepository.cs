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
        return await _dbSet
            .AsNoTracking()
            .Where(proposal =>
                proposal.RequestedByUserId == userId &&
                proposal.Status == ProposalStatus.PendingReview)
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
            cancellationToken);
    }

    public async Task<IEnumerable<AutomationProposal>> GetByBoardIdAsync(Guid boardId, int limit = 100, CancellationToken cancellationToken = default)
    {
        return await GetLimitedWithOperationsAsync(
            _dbSet.Where(p => p.BoardId == boardId),
            limit,
            cancellationToken);
    }

    public async Task<IEnumerable<AutomationProposal>> GetByUserIdAsync(Guid userId, int limit = 100, CancellationToken cancellationToken = default)
    {
        return await GetLimitedWithOperationsAsync(
            _dbSet.Where(p => p.RequestedByUserId == userId),
            limit,
            cancellationToken);
    }

    public async Task<IEnumerable<AutomationProposal>> GetByRiskLevelAsync(RiskLevel riskLevel, int limit = 100, CancellationToken cancellationToken = default)
    {
        return await GetLimitedWithOperationsAsync(
            _dbSet.Where(p => p.RiskLevel == riskLevel),
            limit,
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

    public async Task<IEnumerable<AutomationProposal>> GetExpiredAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return await _dbSet
            .Include(p => p.Operations)
            .Where(p => p.Status == ProposalStatus.PendingReview && p.ExpiresAt < now)
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<AutomationProposal>> GetLimitedWithOperationsAsync(
        IQueryable<AutomationProposal> baseQuery,
        int limit,
        CancellationToken cancellationToken)
    {
        var boundedLimit = limit <= 0 ? DefaultLimit : limit;
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
