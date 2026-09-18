using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

/// <summary>
/// Reads status/scope candidates only. Operation target/action predicates are
/// intentionally forbidden here because revisions can replace those fields.
/// </summary>
public sealed class ProposalEvidenceCandidateStore(TaskdeckDbContext context)
    : IProposalEvidenceCandidateStore
{
    private const int MaximumPageSize = 200;

    public Task<IReadOnlyList<AutomationProposal>> ReadPendingPageAsync(
        ProposalEvidenceScope scope,
        int offset,
        int limit,
        CancellationToken cancellationToken = default) =>
        ReadPageAsync(CandidateSet.Pending, scope, offset, limit, cancellationToken);

    public Task<IReadOnlyList<AutomationProposal>> ReadHistoryPageAsync(
        ProposalEvidenceScope scope,
        int offset,
        int limit,
        CancellationToken cancellationToken = default) =>
        ReadPageAsync(CandidateSet.History, scope, offset, limit, cancellationToken);

    public Task<IReadOnlyList<AutomationProposal>> ReadTerminalPageAsync(
        ProposalEvidenceScope scope,
        int offset,
        int limit,
        CancellationToken cancellationToken = default) =>
        ReadPageAsync(CandidateSet.Terminal, scope, offset, limit, cancellationToken);

    private async Task<IReadOnlyList<AutomationProposal>> ReadPageAsync(
        CandidateSet set,
        ProposalEvidenceScope scope,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        if (scope.RequestedByUserId == Guid.Empty)
            throw new ArgumentException("Evidence scope requires a requesting user.", nameof(scope));

        offset = Math.Max(0, offset);
        limit = Math.Clamp(limit, 1, MaximumPageSize);

        var page = context.Database.IsSqlite()
            ? await ReadSqlitePageAsync(set, scope, offset, limit, cancellationToken)
            : await ReadRelationalPageAsync(set, scope, offset, limit, cancellationToken);

        return set switch
        {
            CandidateSet.Pending => page
                .OrderByDescending(proposal => proposal.CreatedAt)
                .ThenBy(proposal => proposal.Id)
                .ToList(),
            CandidateSet.History => page
                .OrderByDescending(proposal => proposal.UpdatedAt)
                .ThenBy(proposal => proposal.Id)
                .ToList(),
            CandidateSet.Terminal => page
                .OrderByDescending(proposal => proposal.DecidedAt)
                .ThenByDescending(proposal => proposal.UpdatedAt)
                .ThenBy(proposal => proposal.Id)
                .ToList(),
            _ => throw new ArgumentOutOfRangeException(nameof(set))
        };
    }

    private async Task<List<AutomationProposal>> ReadSqlitePageAsync(
        CandidateSet set,
        ProposalEvidenceScope scope,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var pending = (int)ProposalStatus.PendingReview;
        var applied = (int)ProposalStatus.Applied;
        var rejected = (int)ProposalStatus.Rejected;
        var now = DateTime.UtcNow;

        IQueryable<AutomationProposal> query = (set, scope.BoardId) switch
        {
            (CandidateSet.Pending, Guid boardId) => context.AutomationProposals.FromSqlInterpolated($"""
                SELECT * FROM AutomationProposals
                WHERE Status = {pending}
                  AND ExpiresAt > {now}
                  AND BoardId = {boardId}
                ORDER BY CreatedAt DESC, Id
                LIMIT {limit} OFFSET {offset}
                """),
            (CandidateSet.Pending, null) => context.AutomationProposals.FromSqlInterpolated($"""
                SELECT * FROM AutomationProposals
                WHERE Status = {pending}
                  AND ExpiresAt > {now}
                  AND RequestedByUserId = {scope.RequestedByUserId}
                ORDER BY CreatedAt DESC, Id
                LIMIT {limit} OFFSET {offset}
                """),
            (CandidateSet.History, Guid boardId) => context.AutomationProposals.FromSqlInterpolated($"""
                SELECT * FROM AutomationProposals
                WHERE BoardId = {boardId}
                ORDER BY UpdatedAt DESC, Id
                LIMIT {limit} OFFSET {offset}
                """),
            (CandidateSet.History, null) => context.AutomationProposals.FromSqlInterpolated($"""
                SELECT * FROM AutomationProposals
                WHERE RequestedByUserId = {scope.RequestedByUserId}
                ORDER BY UpdatedAt DESC, Id
                LIMIT {limit} OFFSET {offset}
                """),
            (CandidateSet.Terminal, Guid boardId) => context.AutomationProposals.FromSqlInterpolated($"""
                SELECT * FROM AutomationProposals
                WHERE Status IN ({applied}, {rejected})
                  AND BoardId = {boardId}
                ORDER BY DecidedAt DESC, UpdatedAt DESC, Id
                LIMIT {limit} OFFSET {offset}
                """),
            (CandidateSet.Terminal, null) => context.AutomationProposals.FromSqlInterpolated($"""
                SELECT * FROM AutomationProposals
                WHERE Status IN ({applied}, {rejected})
                  AND RequestedByUserId = {scope.RequestedByUserId}
                ORDER BY DecidedAt DESC, UpdatedAt DESC, Id
                LIMIT {limit} OFFSET {offset}
                """),
            _ => throw new ArgumentOutOfRangeException(nameof(set))
        };

        return await query
            .AsNoTracking()
            .Include(proposal => proposal.Operations)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
    }

    private async Task<List<AutomationProposal>> ReadRelationalPageAsync(
        CandidateSet set,
        ProposalEvidenceScope scope,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        IQueryable<AutomationProposal> query = context.AutomationProposals
            .AsNoTracking()
            .Include(proposal => proposal.Operations);

        query = scope.BoardId is Guid boardId
            ? query.Where(proposal => proposal.BoardId == boardId)
            : query.Where(proposal => proposal.RequestedByUserId == scope.RequestedByUserId);

        query = set switch
        {
            CandidateSet.Pending => query.Where(proposal =>
                proposal.Status == ProposalStatus.PendingReview
                && proposal.ExpiresAt > DateTime.UtcNow),
            CandidateSet.History => query,
            CandidateSet.Terminal => query.Where(proposal =>
                proposal.Status == ProposalStatus.Applied
                || proposal.Status == ProposalStatus.Rejected),
            _ => throw new ArgumentOutOfRangeException(nameof(set))
        };

        var ordered = set switch
        {
            CandidateSet.Pending => query
                .OrderByDescending(proposal => proposal.CreatedAt)
                .ThenBy(proposal => proposal.Id),
            CandidateSet.History => query
                .OrderByDescending(proposal => proposal.UpdatedAt)
                .ThenBy(proposal => proposal.Id),
            CandidateSet.Terminal => query
                .OrderByDescending(proposal => proposal.DecidedAt)
                .ThenByDescending(proposal => proposal.UpdatedAt)
                .ThenBy(proposal => proposal.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(set))
        };

        return await ordered
            .Skip(offset)
            .Take(limit)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
    }

    private enum CandidateSet
    {
        Pending,
        History,
        Terminal
    }
}
