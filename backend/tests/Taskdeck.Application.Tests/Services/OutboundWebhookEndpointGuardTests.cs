using FluentAssertions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class OutboundWebhookEndpointGuardTests
{
    [Fact]
    public async Task IsHostBlockedAsync_ShouldBlockIpv4MappedIpv6PrivateAddress()
    {
        var blocked = await OutboundWebhookEndpointGuard.IsHostBlockedAsync("::ffff:10.0.0.8");

        blocked.Should().BeTrue();
    }

    [Fact]
    public async Task IsHostBlockedAsync_ShouldFailClosedWhenDnsResolutionFails()
    {
        var blocked = await OutboundWebhookEndpointGuard.IsHostBlockedAsync("nonexistent-webhook-host.invalid");

        blocked.Should().BeTrue();
    }
}
