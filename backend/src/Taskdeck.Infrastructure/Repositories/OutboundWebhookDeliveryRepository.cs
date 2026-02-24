using Microsoft.EntityFrameworkCore;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;

namespace Taskdeck.Infrastructure.Repositories;

public sealed class OutboundWebhookDeliveryRepository : Repository<OutboundWebhookDelivery>, IOutboundWebhookDeliveryRepository
{
    private const int DefaultLimit = 100;

    public OutboundWebhookDeliveryRepository(TaskdeckDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<OutboundWebhookDelivery>> GetDuePendingAsync(
        DateTimeOffset now,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var boundedLimit = NormalizeLimit(limit);
        if (_context.Database.IsSqlite())
        {
            return await _context.OutboundWebhookDeliveries
                .FromSqlInterpolated(
                    $"""
                    SELECT d.*
                    FROM OutboundWebhookDeliveries AS d
                    INNER JOIN OutboundWebhookSubscriptions AS s ON s.Id = d.SubscriptionId
                    WHERE d.Status = {(int)WebhookDeliveryStatus.Pending}
                      AND d.NextAttemptAt <= {now}
                      AND s.IsActive = 1
                    ORDER BY d.NextAttemptAt ASC, d.CreatedAt ASC
                    LIMIT {boundedLimit}
                    """)
                .Include(delivery => delivery.Subscription)
                .ToListAsync(cancellationToken);
        }

        return await _context.OutboundWebhookDeliveries
            .Include(delivery => delivery.Subscription)
            .Where(delivery =>
                delivery.Status == WebhookDeliveryStatus.Pending &&
                delivery.NextAttemptAt <= now &&
                delivery.Subscription.IsActive)
            .OrderBy(delivery => delivery.NextAttemptAt)
            .ThenBy(delivery => delivery.CreatedAt)
            .Take(boundedLimit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OutboundWebhookDelivery>> GetBySubscriptionAsync(
        Guid subscriptionId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var boundedLimit = NormalizeLimit(limit);
        if (_context.Database.IsSqlite())
        {
            return await _context.OutboundWebhookDeliveries
                .FromSqlInterpolated(
                    $"""
                    SELECT *
                    FROM OutboundWebhookDeliveries
                    WHERE SubscriptionId = {subscriptionId}
                    ORDER BY CreatedAt DESC
                    LIMIT {boundedLimit}
                    """)
                .ToListAsync(cancellationToken);
        }

        return await _context.OutboundWebhookDeliveries
            .Where(delivery => delivery.SubscriptionId == subscriptionId)
            .OrderByDescending(delivery => delivery.CreatedAt)
            .Take(boundedLimit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OutboundWebhookDelivery>> GetStuckProcessingAsync(
        DateTimeOffset staleBefore,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var boundedLimit = NormalizeLimit(limit);
        if (_context.Database.IsSqlite())
        {
            return await _context.OutboundWebhookDeliveries
                .FromSqlInterpolated(
                    $"""
                    SELECT d.*
                    FROM OutboundWebhookDeliveries AS d
                    INNER JOIN OutboundWebhookSubscriptions AS s ON s.Id = d.SubscriptionId
                    WHERE d.Status = {(int)WebhookDeliveryStatus.Processing}
                      AND d.LastAttemptAt IS NOT NULL
                      AND d.LastAttemptAt <= {staleBefore}
                      AND s.IsActive = 1
                    ORDER BY d.LastAttemptAt ASC
                    LIMIT {boundedLimit}
                    """)
                .Include(delivery => delivery.Subscription)
                .ToListAsync(cancellationToken);
        }

        return await _context.OutboundWebhookDeliveries
            .Include(delivery => delivery.Subscription)
            .Where(delivery =>
                delivery.Status == WebhookDeliveryStatus.Processing &&
                delivery.LastAttemptAt.HasValue &&
                delivery.LastAttemptAt <= staleBefore &&
                delivery.Subscription.IsActive)
            .OrderBy(delivery => delivery.LastAttemptAt)
            .Take(boundedLimit)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryClaimPendingAsync(
        Guid deliveryId,
        DateTimeOffset expectedUpdatedAt,
        DateTimeOffset claimedAt,
        CancellationToken cancellationToken = default)
    {
        var rowsAffected = await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE OutboundWebhookDeliveries
            SET Status = {(int)WebhookDeliveryStatus.Processing},
                LastAttemptAt = {claimedAt},
                UpdatedAt = {claimedAt}
            WHERE Id = {deliveryId}
              AND Status = {(int)WebhookDeliveryStatus.Pending}
              AND UpdatedAt = {expectedUpdatedAt}
              AND NextAttemptAt <= {claimedAt}
            """,
            cancellationToken);

        return rowsAffected > 0;
    }

    public async Task ReloadWithSubscriptionAsync(
        OutboundWebhookDelivery delivery,
        CancellationToken cancellationToken = default)
    {
        await _context.Entry(delivery).ReloadAsync(cancellationToken);
        await _context.Entry(delivery)
            .Reference(entity => entity.Subscription)
            .LoadAsync(cancellationToken);
    }

    private static int NormalizeLimit(int limit)
    {
        return limit <= 0 ? DefaultLimit : limit;
    }
}
