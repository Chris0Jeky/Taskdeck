using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public sealed class OutboundWebhookDeliveryRepository : Repository<OutboundWebhookDelivery>, IOutboundWebhookDeliveryRepository
{
    public OutboundWebhookDeliveryRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<OutboundWebhookDelivery>> GetDuePendingAsync(
        DateTimeOffset now,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var duePending = await _context.OutboundWebhookDeliveries
            .Include(delivery => delivery.Subscription)
            .Where(delivery =>
                delivery.Status == WebhookDeliveryStatus.Pending &&
                delivery.NextAttemptAt <= now &&
                delivery.Subscription.IsActive)
            .ToListAsync(cancellationToken);

        // SQLite cannot translate DateTimeOffset ordering from LINQ; order and limit in memory.
        return duePending
            .OrderBy(delivery => delivery.NextAttemptAt)
            .ThenBy(delivery => delivery.CreatedAt)
            .Take(limit)
            .ToList();
    }

    public async Task<IReadOnlyList<OutboundWebhookDelivery>> GetBySubscriptionAsync(
        Guid subscriptionId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var deliveries = await _context.OutboundWebhookDeliveries
            .Where(delivery => delivery.SubscriptionId == subscriptionId)
            .ToListAsync(cancellationToken);

        return deliveries
            .OrderByDescending(delivery => delivery.CreatedAt)
            .Take(limit)
            .ToList();
    }
}
