using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class CardRepository : Repository<Card>, ICardRepository
{
    public CardRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<Card>> GetByBoardIdAsync(Guid boardId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(c => c.BoardId == boardId)
            .Include(c => c.CardLabels)
                .ThenInclude(cl => cl.Label)
            .OrderBy(c => c.ColumnId)
                .ThenBy(c => c.Position)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Card>> GetByBoardIdsAsync(IEnumerable<Guid> boardIds, CancellationToken cancellationToken = default)
    {
        var materializedBoardIds = boardIds
            .Where(boardId => boardId != Guid.Empty)
            .Distinct()
            .ToList();

        if (materializedBoardIds.Count == 0)
            return [];

        return await _dbSet
            .Where(card => materializedBoardIds.Contains(card.BoardId))
            .Include(card => card.CardLabels)
                .ThenInclude(cardLabel => cardLabel.Label)
            .OrderBy(card => card.BoardId)
                .ThenBy(card => card.ColumnId)
            .ThenBy(card => card.Position)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Card>> GetAgendaByBoardIdsAsync(IEnumerable<Guid> boardIds, CancellationToken cancellationToken = default)
    {
        var materializedBoardIds = boardIds
            .Where(boardId => boardId != Guid.Empty)
            .Distinct()
            .ToList();

        if (materializedBoardIds.Count == 0)
            return [];

        return await _dbSet
            .AsNoTracking()
            .Where(card =>
                materializedBoardIds.Contains(card.BoardId) &&
                (card.IsBlocked || card.DueDate.HasValue))
            .OrderBy(card => card.BoardId)
            .ThenBy(card => card.ColumnId)
            .ThenBy(card => card.Position)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Card>> GetByColumnIdAsync(Guid columnId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(c => c.ColumnId == columnId)
            .Include(c => c.CardLabels)
                .ThenInclude(cl => cl.Label)
            .OrderBy(c => c.Position)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Card>> SearchAsync(
        Guid boardId,
        string? searchText,
        Guid? labelId,
        Guid? columnId,
        CancellationToken cancellationToken = default)
    {
        var query = _dbSet
            .Where(c => c.BoardId == boardId)
            .Include(c => c.CardLabels)
                .ThenInclude(cl => cl.Label)
            .AsQueryable();

        if (columnId.HasValue)
        {
            query = query.Where(c => c.ColumnId == columnId.Value);
        }

        if (labelId.HasValue)
        {
            query = query.Where(c => c.CardLabels.Any(cl => cl.LabelId == labelId.Value));
        }

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            query = query.Where(c => c.Title.Contains(searchText) || c.Description.Contains(searchText));
        }

        return await query
            .OrderBy(c => c.ColumnId)
                .ThenBy(c => c.Position)
            .ToListAsync(cancellationToken);
    }

    public async Task<Card?> GetByIdWithLabelsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(c => c.CardLabels)
                .ThenInclude(cl => cl.Label)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<Card>> SearchAcrossBoardsAsync(
        IEnumerable<Guid> boardIds,
        string searchText,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        return await SearchAcrossBoardsAsync(boardIds, searchText, maxResults, 0, cancellationToken);
    }

    public async Task<IEnumerable<Card>> SearchAcrossBoardsAsync(
        IEnumerable<Guid> boardIds,
        string searchText,
        int maxResults,
        int offset,
        CancellationToken cancellationToken = default)
    {
        var materializedBoardIds = boardIds.Distinct().ToList();
        if (materializedBoardIds.Count == 0 || string.IsNullOrWhiteSpace(searchText))
            return [];

        return await _dbSet
            .AsNoTracking()
            .Where(c => materializedBoardIds.Contains(c.BoardId))
            .Where(c => c.Title.Contains(searchText) || c.Description.Contains(searchText))
            .Include(c => c.Board)
            .Include(c => c.Column)
            .OrderBy(c => c.BoardId)
                .ThenBy(c => c.Position)
            .Skip(offset)
            .Take(maxResults)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountSearchAcrossBoardsAsync(
        IEnumerable<Guid> boardIds,
        string searchText,
        CancellationToken cancellationToken = default)
    {
        var materializedBoardIds = boardIds.Distinct().ToList();
        if (materializedBoardIds.Count == 0 || string.IsNullOrWhiteSpace(searchText))
            return 0;

        return await _dbSet
            .AsNoTracking()
            .Where(c => materializedBoardIds.Contains(c.BoardId))
            .Where(c => c.Title.Contains(searchText) || c.Description.Contains(searchText))
            .CountAsync(cancellationToken);
    }
}
