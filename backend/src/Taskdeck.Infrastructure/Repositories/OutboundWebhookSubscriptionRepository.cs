using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public sealed class OutboundWebhookSubscriptionRepository : Repository<OutboundWebhookSubscription>, IOutboundWebhookSubscriptionRepository
{
    public OutboundWebhookSubscriptionRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<OutboundWebhookSubscription?> GetByIdForBoardAsync(
        Guid boardId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default)
    {
        return await _context.OutboundWebhookSubscriptions
            .FirstOrDefaultAsync(
                subscription => subscription.BoardId == boardId && subscription.Id == subscriptionId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<OutboundWebhookSubscription>> GetActiveByBoardAsync(
        Guid boardId,
        CancellationToken cancellationToken = default)
    {
        if (_context.Database.IsSqlite())
        {
            return await _context.OutboundWebhookSubscriptions
                .FromSqlInterpolated(
                    $"SELECT * FROM OutboundWebhookSubscriptions WHERE BoardId = {boardId} AND IsActive = 1 ORDER BY CreatedAt DESC")
                .ToListAsync(cancellationToken);
        }

        return await _context.OutboundWebhookSubscriptions
            .Where(subscription => subscription.BoardId == boardId && subscription.IsActive)
            .OrderByDescending(subscription => subscription.CreatedAt)
            .ToListAsync(cancellationToken);
    }
}
