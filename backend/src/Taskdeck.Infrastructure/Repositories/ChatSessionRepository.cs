using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class ChatSessionRepository : Repository<ChatSession>, IChatSessionRepository
{
    private const int DefaultLimit = 100;

    public ChatSessionRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<ChatSession>> GetByUserIdAsync(Guid userId, int limit = 100, CancellationToken cancellationToken = default)
    {
        return await GetLimitedOrderedByUpdatedAtAsync(
            _dbSet.Where(s => s.UserId == userId),
            limit,
            cancellationToken);
    }

    public async Task<IEnumerable<ChatSession>> GetByBoardIdAsync(Guid boardId, int limit = 100, CancellationToken cancellationToken = default)
    {
        return await GetLimitedOrderedByUpdatedAtAsync(
            _dbSet.Where(s => s.BoardId == boardId),
            limit,
            cancellationToken);
    }

    public async Task<IEnumerable<ChatSession>> GetByStatusAsync(ChatSessionStatus status, int limit = 100, CancellationToken cancellationToken = default)
    {
        return await GetLimitedOrderedByUpdatedAtAsync(
            _dbSet.Where(s => s.Status == status),
            limit,
            cancellationToken);
    }

    public async Task<ChatSession?> GetByIdWithMessagesAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    private static async Task<IReadOnlyList<ChatSession>> GetLimitedOrderedByUpdatedAtAsync(
        IQueryable<ChatSession> query,
        int limit,
        CancellationToken cancellationToken)
    {
        var boundedLimit = limit <= 0 ? DefaultLimit : limit;
        var sessions = await query.ToListAsync(cancellationToken);

        return sessions
            .OrderByDescending(s => s.UpdatedAt)
            .Take(boundedLimit)
            .ToList();
    }
}
