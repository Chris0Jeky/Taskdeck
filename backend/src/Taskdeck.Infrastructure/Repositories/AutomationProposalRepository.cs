using System.Text;
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
            nameof(AutomationProposal.Status),
            status,
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
            nameof(AutomationProposal.BoardId),
            boardId,
            limit,
            includeDeferred: false,
            cancellationToken);
    }

    public async Task<IEnumerable<AutomationProposal>> GetByUserIdAsync(Guid userId, int limit = 100, bool includeDeferred = false, CancellationToken cancellationToken = default)
    {
        return await GetLimitedWithOperationsAsync(
            nameof(AutomationProposal.RequestedByUserId),
            userId,
            limit,
            includeDeferred,
            cancellationToken);
    }

    public async Task<IEnumerable<AutomationProposal>> GetByRiskLevelAsync(RiskLevel riskLevel, int limit = 100, CancellationToken cancellationToken = default)
    {
        return await GetLimitedWithOperationsAsync(
            nameof(AutomationProposal.RiskLevel),
            riskLevel,
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

    // Single-column equality filters behind the bounded list reads. The column name reaches raw SQL, so it
    // MUST be a fixed property/column constant (never user input); the value is always a bound parameter.
    private static readonly HashSet<string> AllowedFilterColumns =
    [
        nameof(AutomationProposal.RequestedByUserId),
        nameof(AutomationProposal.BoardId),
        nameof(AutomationProposal.Status),
        nameof(AutomationProposal.RiskLevel),
    ];

    /// <summary>
    /// Returns up to <paramref name="boundedLimit"/> proposals matching a single-column equality filter,
    /// newest-first by <c>CreatedAt</c> (with <c>Id</c> as a deterministic tiebreaker), with their
    /// operations included.
    /// </summary>
    /// <remarks>
    /// Ordering keys on <c>CreatedAt</c> -- the display order -- NOT on <c>ExpiresAt</c>: ADR-0042 pushes a
    /// deferred proposal's <c>ExpiresAt</c> to <c>DeferredUntil + 24h grace</c> (beyond the normal TTL), so a
    /// resurfaced deferred proposal (<c>DeferredUntil &lt;= now</c>, passing the visibility filter) carries an
    /// inflated <c>ExpiresAt</c> and, under the old <c>ExpiresAt</c> ordering, could occupy a top slot and
    /// evict a fresher pending proposal from a full page (#1247). The top-N is bounded at the database (no
    /// over-fetch): <c>CreatedAt</c> is a <c>DateTimeOffset</c>, which EF Core + SQLite cannot translate in a
    /// LINQ <c>ORDER BY</c> (ADR-0023; <c>ExpiresAt</c> is a <c>DateTime</c>, which is why the old ordering
    /// translated), so the SQLite path pushes <c>ORDER BY CreatedAt DESC, Id LIMIT</c> into raw SQL -- the
    /// same in-DB pattern as <see cref="NotificationRepository"/> / AgentRunRepository -- and re-sorts in
    /// memory because the <c>Include</c> wrapper does not guarantee the raw ORDER BY survives.
    /// </remarks>
    private async Task<IReadOnlyList<AutomationProposal>> GetLimitedWithOperationsAsync(
        string filterColumn,
        object filterValue,
        int limit,
        bool includeDeferred,
        CancellationToken cancellationToken)
    {
        if (!AllowedFilterColumns.Contains(filterColumn))
            throw new ArgumentOutOfRangeException(nameof(filterColumn), filterColumn, "Unsupported filter column.");

        var now = DateTime.UtcNow;
        var boundedLimit = limit <= 0 ? DefaultLimit : limit;

        if (_context.Database.IsSqlite())
        {
            // filterColumn is validated against AllowedFilterColumns above (fixed constants), so interpolating
            // it as an identifier is injection-safe; every value is a {N} bound parameter. Enums are stored as
            // int, so coerce them for the equality bind.
            var boundValue = filterValue is Enum enumValue ? Convert.ToInt32(enumValue) : filterValue;
            var sql = new StringBuilder($"SELECT * FROM AutomationProposals WHERE {filterColumn} = {{0}}");
            var parameters = new List<object> { boundValue };

            if (!includeDeferred)
            {
                // Hide snoozed PENDING proposals (DeferredUntil in the future), but keep decided/terminal
                // proposals visible regardless of a stale snooze value. GDPR export opts out (includeDeferred).
                sql.Append(" AND (Status != {").Append(parameters.Count).Append('}');
                parameters.Add((int)ProposalStatus.PendingReview);
                sql.Append(" OR DeferredUntil IS NULL OR DeferredUntil <= {").Append(parameters.Count).Append("})");
                parameters.Add(now);
            }

            sql.Append(" ORDER BY CreatedAt DESC, Id LIMIT {").Append(parameters.Count).Append('}');
            parameters.Add(boundedLimit);

            var rows = await _dbSet
                .FromSqlRaw(sql.ToString(), parameters.ToArray())
                .Include(p => p.Operations)
                .ToListAsync(cancellationToken);

            // The FromSqlRaw + Include subquery does not guarantee the inner ORDER BY survives to the outer
            // result (see GetByUserAsync); the LIMIT still selected the correct top-N, so re-sort for display.
            return rows
                .OrderByDescending(p => p.CreatedAt)
                .ThenBy(p => p.Id)
                .ToList();
        }

        // Non-SQLite providers (e.g. the Postgres Testcontainer path) translate DateTimeOffset ordering
        // natively, so keep the strongly-typed LINQ query and push ORDER BY + Take into SQL.
        var query = filterColumn switch
        {
            nameof(AutomationProposal.RequestedByUserId) => _dbSet.Where(p => p.RequestedByUserId == (Guid)filterValue),
            nameof(AutomationProposal.BoardId) => _dbSet.Where(p => p.BoardId == (Guid)filterValue),
            nameof(AutomationProposal.Status) => _dbSet.Where(p => p.Status == (ProposalStatus)filterValue),
            _ => _dbSet.Where(p => p.RiskLevel == (RiskLevel)filterValue),
        };

        if (!includeDeferred)
        {
            query = query.Where(p =>
                p.Status != ProposalStatus.PendingReview ||
                p.DeferredUntil == null ||
                p.DeferredUntil <= now);
        }

        return await query
            .Include(p => p.Operations)
            .OrderByDescending(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .Take(boundedLimit)
            .ToListAsync(cancellationToken);
    }
}
