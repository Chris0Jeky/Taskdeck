using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class LabelRepository : Repository<Label>, ILabelRepository
{
    public LabelRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<Label>> GetByBoardIdAsync(Guid boardId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(l => l.BoardId == boardId)
            .OrderBy(l => l.Name)
            .ToListAsync(cancellationToken);
    }
}
