using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class BoardRepository : Repository<Board>, IBoardRepository
{
    public BoardRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<int> CountReadableByUserIdAsync(
        Guid userId,
        bool includeArchived,
        CancellationToken cancellationToken = default)
    {
        return await BuildReadableQuery(userId, includeArchived).CountAsync(cancellationToken);
    }

    public async Task<int> CountReadableUpdatedSinceAsync(
        Guid userId,
        DateTimeOffset updatedSince,
        bool includeArchived,
        CancellationToken cancellationToken = default)
    {
        var boards = await BuildReadableQuery(userId, includeArchived).ToListAsync(cancellationToken);
        return boards.Count(board => board.UpdatedAt >= updatedSince);
    }

    public async Task<IEnumerable<Board>> GetRecentReadableByUserIdAsync(
        Guid userId,
        int limit,
        bool includeArchived,
        CancellationToken cancellationToken = default)
    {
        var boundedLimit = limit <= 0 ? 1 : limit;
        var boards = await BuildReadableQuery(userId, includeArchived).ToListAsync(cancellationToken);

        return boards
            .OrderByDescending(board => board.UpdatedAt)
            .ThenByDescending(board => board.CreatedAt)
            .Take(boundedLimit)
            .ToList();
    }

    public async Task<IEnumerable<Board>> SearchAsync(string? searchText, bool includeArchived, CancellationToken cancellationToken = default)
    {
        var query = BuildSearchQuery(searchText, includeArchived);

        // Load from database first, then order in memory (SQLite doesn't support DateTimeOffset in ORDER BY)
        var boards = await query.ToListAsync(cancellationToken);
        return boards.OrderByDescending(b => b.CreatedAt);
    }

    public async Task<IEnumerable<Guid>> SearchIdsAsync(string? searchText, bool includeArchived, CancellationToken cancellationToken = default)
    {
        var query = BuildSearchQuery(searchText, includeArchived);
        var boardIds = await query.Select(board => board.Id).ToListAsync(cancellationToken);
        return boardIds;
    }

    public async Task<IEnumerable<Board>> GetByIdsAsync(IEnumerable<Guid> boardIds, CancellationToken cancellationToken = default)
    {
        var idSet = boardIds.Distinct().ToList();
        if (idSet.Count == 0)
            return Array.Empty<Board>();

        var boards = await _dbSet
            .Where(board => idSet.Contains(board.Id))
            .ToListAsync(cancellationToken);

        return boards.OrderByDescending(board => board.CreatedAt);
    }

    public async Task<IEnumerable<Guid>> GetOwnedBoardIdsAsync(
        Guid userId,
        IEnumerable<Guid> candidateBoardIds,
        CancellationToken cancellationToken = default)
    {
        var candidateIdSet = candidateBoardIds.Distinct().ToList();
        if (candidateIdSet.Count == 0)
            return Array.Empty<Guid>();

        return await _dbSet
            .Where(board => board.OwnerId == userId && candidateIdSet.Contains(board.Id))
            .Select(board => board.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<Board?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(b => b.Columns)
                .ThenInclude(c => c.Cards)
                    .ThenInclude(card => card.CardLabels)
                        .ThenInclude(cardLabel => cardLabel.Label)
            .Include(b => b.Labels)
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
    }

    private IQueryable<Board> BuildSearchQuery(string? searchText, bool includeArchived)
    {
        var query = _dbSet.AsQueryable();

        if (!includeArchived)
        {
            query = query.Where(board => !board.IsArchived);
        }

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            query = query.Where(board =>
                board.Name.Contains(searchText) ||
                (board.Description != null && board.Description.Contains(searchText)));
        }

        return query;
    }

    private IQueryable<Board> BuildReadableQuery(Guid userId, bool includeArchived)
    {
        var query = _dbSet
            .AsNoTracking()
            .Where(board =>
                board.OwnerId == userId ||
                board.BoardAccesses.Any(access => access.UserId == userId));

        if (!includeArchived)
        {
            query = query.Where(board => !board.IsArchived);
        }

        return query;
    }
}
