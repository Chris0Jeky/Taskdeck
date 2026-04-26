using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IAutomationProposalRepository : IRepository<AutomationProposal>
{
    Task<bool> HasReviewedByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<int> CountPendingReviewByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IEnumerable<AutomationProposal>> GetByStatusAsync(ProposalStatus status, int limit = 100, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AutomationProposal>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    Task<IEnumerable<AutomationProposal>> GetByBoardIdAsync(Guid boardId, int limit = 100, CancellationToken cancellationToken = default);
    Task<IEnumerable<AutomationProposal>> GetByUserIdAsync(Guid userId, int limit = 100, CancellationToken cancellationToken = default);
    Task<IEnumerable<AutomationProposal>> GetByRiskLevelAsync(RiskLevel riskLevel, int limit = 100, CancellationToken cancellationToken = default);
    Task<AutomationProposal?> GetBySourceReferenceAsync(ProposalSourceType sourceType, string referenceId, CancellationToken cancellationToken = default);
    Task<AutomationProposal?> GetByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken = default);
    Task<AutomationProposal?> GetLatestByOperationTargetAsync(string targetType, string targetId, CancellationToken cancellationToken = default);
    Task<AutomationProposal?> GetLatestByOperationTargetAsync(
        string targetType,
        string targetId,
        string actionType,
        ProposalSourceType sourceType,
        CancellationToken cancellationToken = default);
    Task<IEnumerable<AutomationProposal>> GetExpiredAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets proposals that have at least one operation matching the given action type
    /// and are in a terminal state (Applied or Rejected), ordered by most recent first.
    /// Optionally scoped to a specific board. Limited to a lookback window for performance.
    /// </summary>
    Task<IReadOnlyList<AutomationProposal>> GetTerminalByActionTypeAsync(
        string actionType,
        Guid? boardId,
        Guid userId,
        int limit = 100,
        CancellationToken cancellationToken = default);
}
