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
        var boundedLimit = NormalizeLimit(limit);
        if (_context.Database.IsSqlite())
        {
            // SQLite cannot translate DateTimeOffset ordering from LINQ; use raw SQL to keep ORDER BY + LIMIT in DB.
            return await _dbSet
                .FromSqlInterpolated(
                    $"SELECT * FROM ChatSessions WHERE UserId = {userId} ORDER BY UpdatedAt DESC LIMIT {boundedLimit}")
                .Include(s => s.Messages)
                .ToListAsync(cancellationToken);
        }

        return await GetLimitedOrderedByUpdatedAtAsync(
            _dbSet
                .Where(s => s.UserId == userId)
                .Include(s => s.Messages),
            boundedLimit,
            cancellationToken);
    }

    public async Task<IEnumerable<ChatSession>> GetByBoardIdAsync(Guid boardId, int limit = 100, CancellationToken cancellationToken = default)
    {
        var boundedLimit = NormalizeLimit(limit);
        if (_context.Database.IsSqlite())
        {
            // SQLite cannot translate DateTimeOffset ordering from LINQ; use raw SQL to keep ORDER BY + LIMIT in DB.
            return await _dbSet
                .FromSqlInterpolated(
                    $"SELECT * FROM ChatSessions WHERE BoardId = {boardId} ORDER BY UpdatedAt DESC LIMIT {boundedLimit}")
                .ToListAsync(cancellationToken);
        }

        return await GetLimitedOrderedByUpdatedAtAsync(
            _dbSet.Where(s => s.BoardId == boardId),
            boundedLimit,
            cancellationToken);
    }

    public async Task<IEnumerable<ChatSession>> GetByStatusAsync(ChatSessionStatus status, int limit = 100, CancellationToken cancellationToken = default)
    {
        var boundedLimit = NormalizeLimit(limit);
        if (_context.Database.IsSqlite())
        {
            // SQLite cannot translate DateTimeOffset ordering from LINQ; use raw SQL to keep ORDER BY + LIMIT in DB.
            return await _dbSet
                .FromSqlInterpolated(
                    $"SELECT * FROM ChatSessions WHERE Status = {(int)status} ORDER BY UpdatedAt DESC LIMIT {boundedLimit}")
                .ToListAsync(cancellationToken);
        }

        return await GetLimitedOrderedByUpdatedAtAsync(
            _dbSet.Where(s => s.Status == status),
            boundedLimit,
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
        return await query
            .OrderByDescending(s => s.UpdatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    private static int NormalizeLimit(int limit)
    {
        return limit <= 0 ? DefaultLimit : limit;
    }
}
