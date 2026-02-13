using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class AutomationProposalRepository : Repository<AutomationProposal>, IAutomationProposalRepository
{
    public AutomationProposalRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public override async Task<AutomationProposal?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(p => p.Operations)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<AutomationProposal>> GetByStatusAsync(ProposalStatus status, int limit = 100, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(p => p.Operations)
            .Where(p => p.Status == status)
            .OrderByDescending(p => p.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<AutomationProposal>> GetByBoardIdAsync(Guid boardId, int limit = 100, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(p => p.Operations)
            .Where(p => p.BoardId == boardId)
            .OrderByDescending(p => p.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<AutomationProposal>> GetByUserIdAsync(Guid userId, int limit = 100, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(p => p.Operations)
            .Where(p => p.RequestedByUserId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<AutomationProposal>> GetByRiskLevelAsync(RiskLevel riskLevel, int limit = 100, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(p => p.Operations)
            .Where(p => p.RiskLevel == riskLevel)
            .OrderByDescending(p => p.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
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

    public async Task<IEnumerable<AutomationProposal>> GetExpiredAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return await _dbSet
            .Include(p => p.Operations)
            .Where(p => p.Status == ProposalStatus.PendingReview && p.ExpiresAt < now)
            .ToListAsync(cancellationToken);
    }
}
