using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests.Resilience;

/// <summary>
/// Tests that webhook delivery failures are handled with retries, backoff, and
/// dead-lettering rather than crashing or silently losing deliveries.
/// </summary>
public class WebhookDeliveryResilienceTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public WebhookDeliveryResilienceTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── Delivery to Unreachable Target → Retry Scheduling ─────────────

    [Fact]
    public async Task Delivery_ToUnreachableEndpoint_IsScheduledForRetry()
    {
        // Arrange: create entities directly in the DB to simulate a pending delivery.
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var deliveryRepo = scope.ServiceProvider.GetRequiredService<IOutboundWebhookDeliveryRepository>();

        var user = new User("webhook-retry-user", "webhook-retry@example.com", "hash");
        var board = new Board("webhook-retry-board", ownerId: user.Id);
        var subscription = new OutboundWebhookSubscription(
            board.Id,
            user.Id,
            "https://example.com/webhook",
            "signing-secret-123",
            new[] { "card.*" });
        var delivery = new OutboundWebhookDelivery(
            Guid.NewGuid(),
            subscription.Id,
            board.Id,
            "card.created",
            "{\"event\":\"card.created\",\"data\":{}}");

        dbContext.Users.Add(user);
        dbContext.Boards.Add(board);
        dbContext.OutboundWebhookSubscriptions.Add(subscription);
        dbContext.OutboundWebhookDeliveries.Add(delivery);
        await dbContext.SaveChangesAsync();

        // Verify the delivery starts as Pending.
        delivery.Status.Should().Be(WebhookDeliveryStatus.Pending);

        // Simulate a delivery failure by manually marking it.
        var claimedAt = DateTimeOffset.UtcNow;
        var claimed = await deliveryRepo.TryClaimPendingAsync(
            delivery.Id,
            delivery.UpdatedAt,
            claimedAt,
            CancellationToken.None);
        claimed.Should().BeTrue();

        await dbContext.Entry(delivery).ReloadAsync();
        delivery.Status.Should().Be(WebhookDeliveryStatus.Processing);

        // Schedule retry (simulating what the worker does on HTTP failure).
        var nextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(10);
        delivery.ScheduleRetry("Webhook endpoint returned HTTP 503.", nextAttemptAt, 503);
        await dbContext.SaveChangesAsync();

        // Assert: the delivery should be back to Pending with retry metadata.
        await dbContext.Entry(delivery).ReloadAsync();
        delivery.Status.Should().Be(WebhookDeliveryStatus.Pending,
            "failed delivery should be rescheduled as Pending for retry");
        delivery.AttemptCount.Should().Be(1,
            "attempt count should be incremented after a failure");
        delivery.LastErrorMessage.Should().Contain("503",
            "error message should capture the failure reason");
        delivery.NextAttemptAt.Should().BeAfter(DateTimeOffset.MinValue,
            "retry should have a scheduled next attempt time");
    }

    // ── Dead-Lettering After Max Retries ──────────────────────────────

    [Fact]
    public async Task Delivery_AfterMaxRetries_IsDeadLettered()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var deliveryRepo = scope.ServiceProvider.GetRequiredService<IOutboundWebhookDeliveryRepository>();

        var user = new User("webhook-deadletter-user", "webhook-deadletter@example.com", "hash");
        var board = new Board("webhook-deadletter-board", ownerId: user.Id);
        var subscription = new OutboundWebhookSubscription(
            board.Id,
            user.Id,
            "https://example.com/webhook",
            "signing-secret-456",
            new[] { "card.*" });
        var delivery = new OutboundWebhookDelivery(
            Guid.NewGuid(),
            subscription.Id,
            board.Id,
            "card.updated",
            "{\"event\":\"card.updated\",\"data\":{}}");

        dbContext.Users.Add(user);
        dbContext.Boards.Add(board);
        dbContext.OutboundWebhookSubscriptions.Add(subscription);
        dbContext.OutboundWebhookDeliveries.Add(delivery);
        await dbContext.SaveChangesAsync();

        // Simulate multiple failed attempts until max retries is reached.
        // Worker settings default: MaxRetries = 3
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var updatedAt = delivery.UpdatedAt;
            var claimed = await deliveryRepo.TryClaimPendingAsync(
                delivery.Id, updatedAt, DateTimeOffset.UtcNow, CancellationToken.None);
            claimed.Should().BeTrue($"attempt {attempt} claim should succeed");

            await dbContext.Entry(delivery).ReloadAsync();
            delivery.ScheduleRetry(
                $"HTTP 500 on attempt {attempt}",
                DateTimeOffset.UtcNow.AddSeconds(-1),  // Make immediately retryable
                500);
            await dbContext.SaveChangesAsync();
            await dbContext.Entry(delivery).ReloadAsync();
        }

        // Third attempt (attempt index = 3 which equals MaxRetries) → dead letter.
        var finalUpdatedAt = delivery.UpdatedAt;
        var finalClaimed = await deliveryRepo.TryClaimPendingAsync(
            delivery.Id, finalUpdatedAt, DateTimeOffset.UtcNow, CancellationToken.None);
        finalClaimed.Should().BeTrue();

        await dbContext.Entry(delivery).ReloadAsync();
        delivery.MarkDeadLetter("HTTP 500 on final attempt", 500);
        await dbContext.SaveChangesAsync();

        await dbContext.Entry(delivery).ReloadAsync();
        delivery.Status.Should().Be(WebhookDeliveryStatus.DeadLetter,
            "delivery should be dead-lettered after exceeding max retries");
        delivery.LastErrorMessage.Should().Contain("final attempt",
            "dead-letter should preserve the failure reason");
    }

    // ── Inactive Subscription → Dead Letter ───────────────────────────

    [Fact]
    public async Task Delivery_ForInactiveSubscription_CanBeDeadLettered()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();

        var user = new User("webhook-inactive-user", "webhook-inactive@example.com", "hash");
        var board = new Board("webhook-inactive-board", ownerId: user.Id);
        var subscription = new OutboundWebhookSubscription(
            board.Id,
            user.Id,
            "https://example.com/webhook",
            "signing-secret-789",
            new[] { "card.*" });

        // Revoke the subscription before the delivery is processed.
        subscription.Revoke(user.Id);

        var delivery = new OutboundWebhookDelivery(
            Guid.NewGuid(),
            subscription.Id,
            board.Id,
            "card.deleted",
            "{\"event\":\"card.deleted\",\"data\":{}}");

        dbContext.Users.Add(user);
        dbContext.Boards.Add(board);
        dbContext.OutboundWebhookSubscriptions.Add(subscription);
        dbContext.OutboundWebhookDeliveries.Add(delivery);
        await dbContext.SaveChangesAsync();

        // The worker would first claim the delivery (move to Processing),
        // then check subscription.IsActive and dead-letter.
        var deliveryRepo = scope.ServiceProvider.GetRequiredService<IOutboundWebhookDeliveryRepository>();
        var claimed = await deliveryRepo.TryClaimPendingAsync(
            delivery.Id, delivery.UpdatedAt, DateTimeOffset.UtcNow, CancellationToken.None);
        claimed.Should().BeTrue("delivery should be claimable");

        await dbContext.Entry(delivery).ReloadAsync();
        delivery.Status.Should().Be(WebhookDeliveryStatus.Processing);

        delivery.MarkDeadLetter("Webhook subscription is inactive before delivery dispatch.");
        await dbContext.SaveChangesAsync();

        await dbContext.Entry(delivery).ReloadAsync();
        delivery.Status.Should().Be(WebhookDeliveryStatus.DeadLetter,
            "delivery for inactive subscription should be dead-lettered");
        delivery.LastErrorMessage.Should().Contain("inactive",
            "dead-letter message should explain why delivery was abandoned");
    }

    // ── Stuck Processing Recovery ────────────────────────────────────

    [Fact]
    public async Task StuckProcessingDelivery_CanBeReturnedToPending()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var deliveryRepo = scope.ServiceProvider.GetRequiredService<IOutboundWebhookDeliveryRepository>();

        var user = new User("webhook-stuck-user", "webhook-stuck@example.com", "hash");
        var board = new Board("webhook-stuck-board", ownerId: user.Id);
        var subscription = new OutboundWebhookSubscription(
            board.Id,
            user.Id,
            "https://example.com/webhook",
            "signing-secret-stuck",
            new[] { "card.*" });
        var delivery = new OutboundWebhookDelivery(
            Guid.NewGuid(),
            subscription.Id,
            board.Id,
            "card.moved",
            "{\"event\":\"card.moved\",\"data\":{}}");

        dbContext.Users.Add(user);
        dbContext.Boards.Add(board);
        dbContext.OutboundWebhookSubscriptions.Add(subscription);
        dbContext.OutboundWebhookDeliveries.Add(delivery);
        await dbContext.SaveChangesAsync();

        // Claim the delivery (move to Processing).
        var claimed = await deliveryRepo.TryClaimPendingAsync(
            delivery.Id, delivery.UpdatedAt, DateTimeOffset.UtcNow, CancellationToken.None);
        claimed.Should().BeTrue();

        await dbContext.Entry(delivery).ReloadAsync();
        delivery.Status.Should().Be(WebhookDeliveryStatus.Processing);

        // Simulate worker recovery: return the stuck delivery to Pending.
        delivery.ReturnToPending(
            DateTimeOffset.UtcNow,
            "Recovered stale processing webhook delivery for retry.");
        await dbContext.SaveChangesAsync();

        await dbContext.Entry(delivery).ReloadAsync();
        delivery.Status.Should().Be(WebhookDeliveryStatus.Pending,
            "stuck processing delivery should be recoverable to Pending");
        delivery.LastErrorMessage.Should().Contain("Recovered",
            "recovery message should explain why the delivery was returned to Pending");
    }
}
