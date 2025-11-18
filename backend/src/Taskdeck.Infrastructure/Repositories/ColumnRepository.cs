using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class ColumnRepository : Repository<Column>, IColumnRepository
{
    public ColumnRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<Column>> GetByBoardIdAsync(Guid boardId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(c => c.BoardId == boardId)
            .Include(c => c.Cards)
            .OrderBy(c => c.Position)
            .ToListAsync(cancellationToken);
    }

    public async Task<Column?> GetByIdWithCardsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(c => c.Cards)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }
}
