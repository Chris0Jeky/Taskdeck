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
            // SQLite persists DateTimeOffset values as offset-bearing TEXT. Lexical comparison is
            // correct only when both operands use the same offset, so normalize the query bound to
            // the UTC representation used by Taskdeck's persisted delivery timestamps. Keep the
            // provider-native LINQ path below unchanged: relational providers with temporal types
            // compare instants rather than serialized text.
            var normalizedNow = now.ToUniversalTime();
            return await _context.OutboundWebhookDeliveries
                .FromSqlInterpolated(
                    $"""
                    SELECT d.*
                    FROM OutboundWebhookDeliveries AS d
                    WHERE d.Status = {(int)WebhookDeliveryStatus.Pending}
                      AND d.NextAttemptAt <= {normalizedNow}
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
                delivery.NextAttemptAt <= now)
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
            // See GetDuePendingAsync: the raw SQLite predicate compares serialized TEXT, so the
            // caller's equivalent non-UTC representation must be canonicalized before binding.
            var normalizedStaleBefore = staleBefore.ToUniversalTime();
            return await _context.OutboundWebhookDeliveries
                .FromSqlInterpolated(
                    $"""
                    SELECT d.*
                    FROM OutboundWebhookDeliveries AS d
                    WHERE d.Status = {(int)WebhookDeliveryStatus.Processing}
                      AND d.LastAttemptAt IS NOT NULL
                      AND d.LastAttemptAt <= {normalizedStaleBefore}
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
                delivery.LastAttemptAt <= staleBefore)
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
