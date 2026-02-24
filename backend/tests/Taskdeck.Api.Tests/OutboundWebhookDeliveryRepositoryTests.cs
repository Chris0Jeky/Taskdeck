using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
using Xunit;

namespace Taskdeck.Api.Tests;

public class OutboundWebhookDeliveryRepositoryTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public OutboundWebhookDeliveryRepositoryTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task TryClaimPendingAsync_ShouldClaimOnlyOnce_ForExpectedVersion()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IOutboundWebhookDeliveryRepository>();

        var user = new User("worker-claimer", "worker-claimer@example.com", "hash");
        var board = new Board("worker-claim-board", ownerId: user.Id);
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

        dbContext.Users.Add(user);
        dbContext.Boards.Add(board);
        dbContext.OutboundWebhookSubscriptions.Add(subscription);
        dbContext.OutboundWebhookDeliveries.Add(delivery);
        await dbContext.SaveChangesAsync();

        var initialUpdatedAt = delivery.UpdatedAt;
        var claimedAt = DateTimeOffset.UtcNow;
        var firstClaim = await repository.TryClaimPendingAsync(
            delivery.Id,
            initialUpdatedAt,
            claimedAt,
            CancellationToken.None);
        firstClaim.Should().BeTrue();

        await dbContext.Entry(delivery).ReloadAsync();
        delivery.Status.Should().Be(WebhookDeliveryStatus.Processing);
        delivery.LastAttemptAt.Should().NotBeNull();

        var secondClaim = await repository.TryClaimPendingAsync(
            delivery.Id,
            initialUpdatedAt,
            claimedAt.AddSeconds(1),
            CancellationToken.None);
        secondClaim.Should().BeFalse();
    }
}
