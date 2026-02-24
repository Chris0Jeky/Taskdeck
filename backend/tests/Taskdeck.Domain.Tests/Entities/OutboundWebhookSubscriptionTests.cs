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
}
