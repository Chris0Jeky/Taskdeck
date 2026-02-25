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

    [Fact]
    public void ReturnToPending_ShouldNotIncrementAttemptCount()
    {
        var nextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(10);
        var delivery = new OutboundWebhookDelivery(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "card.updated",
            "{\"event\":\"card.updated\"}");
        delivery.MarkProcessing();

        delivery.ReturnToPending(nextAttemptAt, "interrupted on shutdown");

        delivery.Status.Should().Be(WebhookDeliveryStatus.Pending);
        delivery.AttemptCount.Should().Be(0);
        delivery.NextAttemptAt.Should().Be(nextAttemptAt);
        delivery.LastErrorMessage.Should().Contain("interrupted on shutdown");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenEventTypeExceedsMaxLength()
    {
        var eventType = $"{new string('a', 120)}x";

        var act = () => new OutboundWebhookDelivery(
            Guid.NewGuid(),
            Guid.NewGuid(),
            eventType,
            "{\"event\":\"card.updated\"}");

        act.Should().Throw<DomainException>()
            .Where(ex => ex.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void ScheduleRetry_ShouldTruncateErrorMessageToPersistenceLimit()
    {
        var delivery = new OutboundWebhookDelivery(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "card.updated",
            "{\"event\":\"card.updated\"}");
        delivery.MarkProcessing();
        var longError = new string('x', 1200);

        delivery.ScheduleRetry(longError, DateTimeOffset.UtcNow.AddMinutes(1));

        delivery.LastErrorMessage.Should().NotBeNull();
        delivery.LastErrorMessage!.Length.Should().Be(1000);
    }

    [Fact]
    public void MarkDeadLetter_ShouldTruncateErrorMessageToPersistenceLimit()
    {
        var delivery = new OutboundWebhookDelivery(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "card.updated",
            "{\"event\":\"card.updated\"}");
        delivery.MarkProcessing();
        var longError = new string('y', 1300);

        delivery.MarkDeadLetter(longError, 500);

        delivery.Status.Should().Be(WebhookDeliveryStatus.DeadLetter);
        delivery.LastResponseStatusCode.Should().Be(500);
        delivery.LastErrorMessage.Should().NotBeNull();
        delivery.LastErrorMessage!.Length.Should().Be(1000);
    }
}
