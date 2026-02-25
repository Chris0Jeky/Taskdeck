using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class OutboundWebhookSubscriptionTests
{
    [Fact]
    public void Constructor_ShouldDefaultEventFiltersToWildcard()
    {
        var subscription = new OutboundWebhookSubscription(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "https://example.com/hook",
            "secret");

        subscription.GetEventFilters().Should().ContainSingle().Which.Should().Be("*");
        subscription.IsActive.Should().BeTrue();
    }

    [Fact]
    public void MatchesEvent_ShouldMatchExactAndNamespaceWildcard()
    {
        var subscription = new OutboundWebhookSubscription(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "https://example.com/hook",
            "secret",
            ["card.updated", "proposal.*"]);

        subscription.MatchesEvent("card.updated").Should().BeTrue();
        subscription.MatchesEvent("proposal.created").Should().BeTrue();
        subscription.MatchesEvent("card.deleted").Should().BeFalse();
    }

    [Fact]
    public void Revoke_ShouldMarkSubscriptionInactive()
    {
        var actorId = Guid.NewGuid();
        var subscription = new OutboundWebhookSubscription(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "https://example.com/hook",
            "secret");

        subscription.Revoke(actorId);

        subscription.IsActive.Should().BeFalse();
        subscription.RevokedByUserId.Should().Be(actorId);
        subscription.RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public void RotateSecret_ShouldThrow_WhenSubscriptionIsRevoked()
    {
        var subscription = new OutboundWebhookSubscription(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "https://example.com/hook",
            "secret");
        subscription.Revoke(Guid.NewGuid());

        var act = () => subscription.RotateSecret("new-secret");

        act.Should().Throw<DomainException>()
            .Where(ex => ex.ErrorCode == ErrorCodes.InvalidOperation);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenEndpointExceedsMaxLength()
    {
        var endpoint = $"https://example.com/{new string('a', 490)}";

        var act = () => new OutboundWebhookSubscription(
            Guid.NewGuid(),
            Guid.NewGuid(),
            endpoint,
            "secret");

        act.Should().Throw<DomainException>()
            .Where(ex => ex.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void RotateSecret_ShouldThrow_WhenSecretExceedsMaxLength()
    {
        var subscription = new OutboundWebhookSubscription(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "https://example.com/hook",
            "secret");
        var longSecret = new string('s', 201);

        var act = () => subscription.RotateSecret(longSecret);

        act.Should().Throw<DomainException>()
            .Where(ex => ex.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenSerializedFiltersExceedMaxLength()
    {
        var filters = Enumerable.Range(0, 20)
            .Select(index => $"{new string((char)('a' + (index % 26)), 18)}.{new string((char)('a' + ((index + 1) % 26)), 18)}")
            .ToList();

        var act = () => new OutboundWebhookSubscription(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "https://example.com/hook",
            "secret",
            filters);

        act.Should().Throw<DomainException>()
            .Where(ex => ex.ErrorCode == ErrorCodes.ValidationError);
    }
}
