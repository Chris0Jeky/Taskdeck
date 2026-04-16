using FluentAssertions;
using Taskdeck.Domain.Connectors;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Domain.Tests.Connectors;

public class ConnectorCapabilitiesTests
{
    [Fact]
    public void Constructor_ShouldSetAllProperties()
    {
        var events = new[] { "push", "pull_request.opened" };
        var authMethods = new[] { ConnectorAuthMethod.PersonalAccessToken };

        var capabilities = new ConnectorCapabilities(
            "GitHub",
            ConnectorDirection.Context,
            events,
            authMethods,
            rateLimitPerMinute: 60,
            description: "GitHub connector");

        capabilities.DisplayName.Should().Be("GitHub");
        capabilities.Direction.Should().Be(ConnectorDirection.Context);
        capabilities.SupportedEventTypes.Should().BeEquivalentTo(events);
        capabilities.AuthMethods.Should().BeEquivalentTo(authMethods);
        capabilities.RateLimitPerMinute.Should().Be(60);
        capabilities.Description.Should().Be("GitHub connector");
    }

    [Fact]
    public void Constructor_ShouldDefaultRateLimitToNull()
    {
        var capabilities = new ConnectorCapabilities(
            "Test",
            ConnectorDirection.Inbound,
            Array.Empty<string>(),
            Array.Empty<ConnectorAuthMethod>());

        capabilities.RateLimitPerMinute.Should().BeNull();
        capabilities.Description.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_ShouldThrowOnNullDisplayName()
    {
        var act = () => new ConnectorCapabilities(
            null!,
            ConnectorDirection.Inbound,
            Array.Empty<string>(),
            Array.Empty<ConnectorAuthMethod>());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ShouldThrowOnNullEventTypes()
    {
        var act = () => new ConnectorCapabilities(
            "Test",
            ConnectorDirection.Inbound,
            null!,
            Array.Empty<ConnectorAuthMethod>());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ShouldThrowOnNullAuthMethods()
    {
        var act = () => new ConnectorCapabilities(
            "Test",
            ConnectorDirection.Inbound,
            Array.Empty<string>(),
            null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
