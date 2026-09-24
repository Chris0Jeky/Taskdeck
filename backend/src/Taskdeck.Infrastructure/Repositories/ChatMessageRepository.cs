using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public class ChatMessageRepository : Repository<ChatMessage>, IChatMessageRepository
{
    public ChatMessageRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<ChatMessage>> GetBySessionIdAsync(Guid sessionId, int limit = 100, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<ChatMessage>> GetByProposalIdAsync(Guid proposalId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(m => m.ProposalId == proposalId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountBySessionIdsAsync(IEnumerable<Guid> sessionIds, CancellationToken cancellationToken = default)
    {
        var idList = sessionIds.ToList();
        if (idList.Count == 0)
            return new Dictionary<Guid, int>();

        var counts = await _dbSet
            .Where(m => idList.Contains(m.SessionId))
            .GroupBy(m => m.SessionId)
            .Select(g => new { SessionId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(x => x.SessionId, x => x.Count);
    }
}
