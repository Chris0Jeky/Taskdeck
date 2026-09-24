using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

public interface IAutomationProposalRepository : IRepository<AutomationProposal>
{
    Task<bool> HasReviewedByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> HasAppliedByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<int> CountPendingReviewByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IEnumerable<AutomationProposal>> GetByStatusAsync(ProposalStatus status, int limit = 100, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AutomationProposal>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    /// <summary>
    /// Loads minimal headers for the given ids in a single query without operation includes,
    /// for batch pre-checks that must not pay full-entity cost per row.
    /// </summary>
    Task<IReadOnlyList<ProposalHeaderDto>> GetHeadersByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    Task<IEnumerable<AutomationProposal>> GetByBoardIdAsync(Guid boardId, int limit = 100, CancellationToken cancellationToken = default);
    // includeDeferred:false (default) hides currently-snoozed pending proposals for review-queue
    // reads; completeness-sensitive callers (GDPR data export) pass includeDeferred:true so a
    // snoozed proposal is never silently dropped from the user's complete data set.
    Task<IEnumerable<AutomationProposal>> GetByUserIdAsync(Guid userId, int limit = 100, bool includeDeferred = false, CancellationToken cancellationToken = default);
    // Active Review read: archived-board proposals are excluded before LIMIT so retained history
    // cannot under-fill the bounded page. Complete/export and explicit board reads use the methods above.
    Task<IEnumerable<AutomationProposal>> GetActiveByUserIdAsync(
        Guid userId,
        int limit = 100,
        ProposalStatus? status = null,
        RiskLevel? riskLevel = null,
        CancellationToken cancellationToken = default);
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

    /// <summary>
    /// Legacy creation-time operation lookup retained for repository compatibility only.
    /// </summary>
    /// <remarks>
    /// Never use this method for conflict, history, similar-past, or other review evidence.
    /// A proposal revision can replace the target represented by the immutable operation row, so
    /// raw-operation filtering can produce both false positives and false negatives. Evidence paths
    /// must read status/scope candidates through <see cref="IProposalEvidenceCandidateStore"/> and
    /// resolve each candidate's effective revision in Application (#2452, #3249).
    /// Deferred pending proposals remain candidates in that revision-aware path.
    /// </remarks>
    Task<IReadOnlyList<AutomationProposal>> GetPendingByOperationTargetAsync(
        string targetType,
        string targetId,
        CancellationToken cancellationToken = default);

    // Automatic-expiry candidate read. Expired PendingReview rows whose board is archived are
    // withheld from Expirable and counted instead: expiry is a decision write, and ADR-0063 / #2168
    // make archived decision history read-only (#2197). Board-less and dangling-board rows stay
    // expirable, matching GetActiveByUserIdAsync's predicate. Because archive may commit after this
    // query, callers must pass Expirable board references through
    // IAutomationPolicyEngine.GuardProposalDecisionWritesAsync immediately before mutation (#2170).
    Task<ExpiredProposalSweep> GetExpiredAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Legacy creation-time action lookup retained for repository compatibility only.
    /// </summary>
    /// <remarks>
    /// Never use this method for review evidence. The approved revision pin or decision-time
    /// revision, not the immutable creation-time action row, defines terminal evidence. Use
    /// <see cref="IProposalEvidenceCandidateStore.ReadTerminalPageAsync"/> followed by effective
    /// revision resolution in Application (#2452, #3249).
    /// </remarks>
    Task<IReadOnlyList<AutomationProposal>> GetTerminalByActionTypeAsync(
        string actionType,
        Guid? boardId,
        Guid userId,
        int limit = 100,
        CancellationToken cancellationToken = default);
}
