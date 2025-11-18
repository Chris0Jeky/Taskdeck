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
}
