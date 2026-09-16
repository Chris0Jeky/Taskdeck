using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Pins instant semantics for caller-supplied DateTimeOffset bounds against SQLite TEXT timestamps.
/// A non-UTC offset representing the same instant must not change due/stale membership (#1422).
/// </summary>
public sealed class OutboundWebhookDeliveryUtcBoundaryTests
    : IClassFixture<HostedWorkerDisabledTestWebApplicationFactory>
{
    private readonly HostedWorkerDisabledTestWebApplicationFactory _factory;

    public OutboundWebhookDeliveryUtcBoundaryTests(
        HostedWorkerDisabledTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetDuePendingAsync_WithNonUtcBound_ComparesByInstant()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IOutboundWebhookDeliveryRepository>();
        var delivery = AddDelivery(dbContext, "due-boundary", processing: false);
        var dueAt = new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);
        dbContext.Entry(delivery).Property(item => item.NextAttemptAt).CurrentValue = dueAt;
        await dbContext.SaveChangesAsync();

        var sameInstantWithNegativeOffset = dueAt.ToOffset(TimeSpan.FromHours(-2));
        var due = await repository.GetDuePendingAsync(
            sameInstantWithNegativeOffset,
            limit: 100,
            CancellationToken.None);

        due.Should().ContainSingle(item => item.Id == delivery.Id);
    }

    [Fact]
    public async Task GetStuckProcessingAsync_WithNonUtcBound_ComparesByInstant()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IOutboundWebhookDeliveryRepository>();
        var delivery = AddDelivery(dbContext, "stuck-boundary", processing: true);
        var attemptedAt = new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);
        dbContext.Entry(delivery).Property(item => item.LastAttemptAt).CurrentValue = attemptedAt;
        await dbContext.SaveChangesAsync();

        var sameInstantWithNegativeOffset = attemptedAt.ToOffset(TimeSpan.FromHours(-2));
        var stuck = await repository.GetStuckProcessingAsync(
            sameInstantWithNegativeOffset,
            limit: 100,
            CancellationToken.None);

        stuck.Should().ContainSingle(item => item.Id == delivery.Id);
    }

    private static OutboundWebhookDelivery AddDelivery(
        TaskdeckDbContext dbContext,
        string scope,
        bool processing)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var user = new User(
            $"webhook-{scope}-{suffix}",
            $"webhook-{scope}-{suffix}@example.com",
            "hash");
        var board = new Board($"webhook-{scope}-{suffix}", ownerId: user.Id);
        var subscription = new OutboundWebhookSubscription(
            board.Id,
            user.Id,
            "https://example.com/webhook",
            "secret",
            ["card.*"]);
        var delivery = new OutboundWebhookDelivery(
            Guid.NewGuid(),
            subscription.Id,
            board.Id,
            "card.updated",
            "{\"event\":\"card.updated\"}");
        if (processing)
        {
            delivery.MarkProcessing();
        }

        dbContext.Users.Add(user);
        dbContext.Boards.Add(board);
        dbContext.OutboundWebhookSubscriptions.Add(subscription);
        dbContext.OutboundWebhookDeliveries.Add(delivery);
        return delivery;
    }
}
