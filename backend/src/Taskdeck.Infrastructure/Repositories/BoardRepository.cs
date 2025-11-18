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

    public async Task<IEnumerable<Board>> SearchAsync(string? searchText, bool includeArchived, CancellationToken cancellationToken = default)
    {
        var query = _dbSet.AsQueryable();

        if (!includeArchived)
        {
            query = query.Where(b => !b.IsArchived);
        }

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            query = query.Where(b => b.Name.Contains(searchText) ||
                                    (b.Description != null && b.Description.Contains(searchText)));
        }

        return await query
            .OrderByDescending(b => b.CreatedAt.Ticks)
            .ToListAsync(cancellationToken);
    }

    public async Task<Board?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(b => b.Columns)
                .ThenInclude(c => c.Cards)
            .Include(b => b.Labels)
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
    }
}
