using FluentAssertions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class OutboundWebhookEndpointGuardTests
{
    [Fact]
    public async Task IsHostBlockedAsync_ShouldBlockIpv4MappedIpv6PrivateAddress()
    {
        var blocked = await OutboundWebhookEndpointGuard.IsHostBlockedAsync(
            "::ffff:10.0.0.8",
            allowLocalhostEndpoints: false);

        blocked.Should().BeTrue();
    }

    [Fact]
    public async Task IsHostBlockedAsync_ShouldFailClosedWhenDnsResolutionFails()
    {
        var blocked = await OutboundWebhookEndpointGuard.IsHostBlockedAsync(
            "nonexistent-webhook-host.invalid",
            allowLocalhostEndpoints: false);

        blocked.Should().BeTrue();
    }

    [Fact]
    public async Task IsHostBlockedAsync_ShouldAllowLocalhost_WhenConfigured()
    {
        var blocked = await OutboundWebhookEndpointGuard.IsHostBlockedAsync(
            "localhost",
            allowLocalhostEndpoints: true);

        blocked.Should().BeFalse();
    }
}
