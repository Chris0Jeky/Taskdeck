using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class OutboundWebhookDeliveryTests
{
    [Fact]
    public void Constructor_ShouldInitializePendingDelivery()
    {
        var delivery = new OutboundWebhookDelivery(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "card.updated",
            "{\"event\":\"card.updated\"}");

        delivery.Status.Should().Be(WebhookDeliveryStatus.Pending);
        delivery.AttemptCount.Should().Be(0);
        delivery.NextAttemptAt.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
    }

    [Fact]
    public void MarkProcessingThenMarkDelivered_ShouldSetDeliveredState()
    {
        var delivery = new OutboundWebhookDelivery(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "card.updated",
            "{\"event\":\"card.updated\"}");

        delivery.MarkProcessing();
        delivery.MarkDelivered(202);

        delivery.Status.Should().Be(WebhookDeliveryStatus.Delivered);
        delivery.AttemptCount.Should().Be(1);
        delivery.LastResponseStatusCode.Should().Be(202);
        delivery.DeliveredAt.Should().NotBeNull();
        delivery.LastErrorMessage.Should().BeNull();
    }

    [Fact]
    public void ScheduleRetry_ShouldReturnToPendingWithIncrementedAttempt()
    {
        var nextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(1);
        var delivery = new OutboundWebhookDelivery(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "card.updated",
            "{\"event\":\"card.updated\"}");

        delivery.MarkProcessing();
        delivery.ScheduleRetry("network timeout", nextAttemptAt, 503);

        delivery.Status.Should().Be(WebhookDeliveryStatus.Pending);
        delivery.AttemptCount.Should().Be(1);
        delivery.NextAttemptAt.Should().Be(nextAttemptAt);
        delivery.LastResponseStatusCode.Should().Be(503);
        delivery.LastErrorMessage.Should().Contain("network timeout");
    }

    [Fact]
    public void MarkProcessing_ShouldThrow_WhenDeliveryIsNotPending()
    {
        var delivery = new OutboundWebhookDelivery(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "card.updated",
            "{\"event\":\"card.updated\"}");
        delivery.MarkProcessing();
        delivery.MarkDelivered();

        var act = () => delivery.MarkProcessing();

        act.Should().Throw<DomainException>()
            .Where(ex => ex.ErrorCode == ErrorCodes.InvalidOperation);
    }
}
