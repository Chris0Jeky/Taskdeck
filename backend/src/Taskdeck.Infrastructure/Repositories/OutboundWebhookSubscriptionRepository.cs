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
        // SQLite cannot translate DateTimeOffset ordering from LINQ; load filtered set first, then order in memory.
        var subscriptions = await _context.OutboundWebhookSubscriptions
            .Where(subscription => subscription.BoardId == boardId && subscription.IsActive)
            .ToListAsync(cancellationToken);

        return subscriptions
            .OrderByDescending(subscription => subscription.CreatedAt)
            .ToList();
    }
}
