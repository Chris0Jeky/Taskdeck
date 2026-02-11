using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

/// <summary>
/// SCAFFOLDING: Placeholder repository implementation for BoardAccess entity.
/// </summary>
public class BoardAccessRepository : Repository<BoardAccess>, IBoardAccessRepository
{
    public BoardAccessRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<BoardAccess?> GetByBoardAndUserAsync(Guid boardId, Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.BoardAccesses
            .Include(ba => ba.User)
            .Include(ba => ba.Board)
            .FirstOrDefaultAsync(ba => ba.BoardId == boardId && ba.UserId == userId, cancellationToken);
    }

    public async Task<IEnumerable<BoardAccess>> GetByBoardIdAsync(Guid boardId, CancellationToken cancellationToken = default)
    {
        return await _context.BoardAccesses
            .Include(ba => ba.User)
            .Where(ba => ba.BoardId == boardId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<BoardAccess>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.BoardAccesses
            .Include(ba => ba.Board)
            .Where(ba => ba.UserId == userId)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> HasAccessAsync(Guid boardId, Guid userId, UserRole? minimumRole = null, CancellationToken cancellationToken = default)
    {
        var query = _context.BoardAccesses
            .Where(ba => ba.BoardId == boardId && ba.UserId == userId);

        if (minimumRole.HasValue)
        {
            query = query.Where(ba => ba.Role <= minimumRole.Value);
        }

        return await query.AnyAsync(cancellationToken);
    }
}
