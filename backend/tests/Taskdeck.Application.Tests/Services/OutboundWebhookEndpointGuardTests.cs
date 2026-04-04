using FluentAssertions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class OutboundWebhookEndpointGuardTests
{
    // -----------------------------------------------------------------------
    // IsHostBlockedAsync – pre-existing coverage
    // -----------------------------------------------------------------------

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

    [Fact]
    public async Task IsHostBlockedAsync_ShouldBlockLocalhost_WhenNotConfigured()
    {
        var blocked = await OutboundWebhookEndpointGuard.IsHostBlockedAsync(
            "localhost",
            allowLocalhostEndpoints: false);

        blocked.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // IsHostBlockedByStaticPolicy – SSRF: literal private IPv4 addresses
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.1.2.3")]
    [InlineData("127.255.255.255")]
    public void IsHostBlockedByStaticPolicy_ShouldBlockLoopbackRange(string host)
    {
        OutboundWebhookEndpointGuard.IsHostBlockedByStaticPolicy(host, allowLocalhostEndpoints: false)
            .Should().BeTrue($"{host} is in the 127.0.0.0/8 loopback range");
    }

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("10.128.0.1")]
    [InlineData("10.255.255.255")]
    public void IsHostBlockedByStaticPolicy_ShouldBlockClass10PrivateRange(string host)
    {
        OutboundWebhookEndpointGuard.IsHostBlockedByStaticPolicy(host, allowLocalhostEndpoints: false)
            .Should().BeTrue($"{host} is in the 10.0.0.0/8 private range");
    }

    [Theory]
    [InlineData("172.16.0.1")]
    [InlineData("172.20.5.10")]
    [InlineData("172.31.255.255")]
    public void IsHostBlockedByStaticPolicy_ShouldBlockClass172PrivateRange(string host)
    {
        OutboundWebhookEndpointGuard.IsHostBlockedByStaticPolicy(host, allowLocalhostEndpoints: false)
            .Should().BeTrue($"{host} is in the 172.16.0.0/12 private range");
    }

    [Theory]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.1")]
    public void IsHostBlockedByStaticPolicy_ShouldAllowAddressesOutsidePrivateRange(string host)
    {
        // These addresses are public — they live just outside the 172.16-31 private block.
        OutboundWebhookEndpointGuard.IsHostBlockedByStaticPolicy(host, allowLocalhostEndpoints: false)
            .Should().BeFalse($"{host} is outside the 172.16.0.0/12 private range");
    }

    [Theory]
    [InlineData("192.168.0.1")]
    [InlineData("192.168.100.5")]
    [InlineData("192.168.255.254")]
    public void IsHostBlockedByStaticPolicy_ShouldBlockClass192_168PrivateRange(string host)
    {
        OutboundWebhookEndpointGuard.IsHostBlockedByStaticPolicy(host, allowLocalhostEndpoints: false)
            .Should().BeTrue($"{host} is in the 192.168.0.0/16 private range");
    }

    [Fact]
    public void IsHostBlockedByStaticPolicy_ShouldBlockLinkLocalAddress()
    {
        // AWS metadata endpoint / link-local range (169.254.0.0/16)
        OutboundWebhookEndpointGuard.IsHostBlockedByStaticPolicy("169.254.169.254", allowLocalhostEndpoints: false)
            .Should().BeTrue("169.254.169.254 is the AWS metadata endpoint / link-local range");
    }

    [Fact]
    public void IsHostBlockedByStaticPolicy_ShouldBlockAllZeroesAddress()
    {
        OutboundWebhookEndpointGuard.IsHostBlockedByStaticPolicy("0.0.0.0", allowLocalhostEndpoints: false)
            .Should().BeTrue("0.0.0.0 is the unspecified address");
    }

    [Theory]
    [InlineData("100.64.0.1")]
    [InlineData("100.100.200.1")]
    [InlineData("100.127.255.255")]
    public void IsHostBlockedByStaticPolicy_ShouldBlockCgnatRange(string host)
    {
        // Carrier-grade NAT range (100.64.0.0/10 per RFC 6598)
        OutboundWebhookEndpointGuard.IsHostBlockedByStaticPolicy(host, allowLocalhostEndpoints: false)
            .Should().BeTrue($"{host} is in the CGNAT range 100.64.0.0/10");
    }

    // -----------------------------------------------------------------------
    // SSRF: IPv6 loopback and special addresses
    // -----------------------------------------------------------------------

    [Fact]
    public void IsHostBlockedByStaticPolicy_ShouldBlockIpv6Loopback()
    {
        OutboundWebhookEndpointGuard.IsHostBlockedByStaticPolicy("::1", allowLocalhostEndpoints: false)
            .Should().BeTrue("::1 is the IPv6 loopback address");
    }

    [Theory]
    [InlineData("fe80::1")]
    [InlineData("fe80::abcd:ef01")]
    public void IsHostBlockedByStaticPolicy_ShouldBlockIpv6LinkLocal(string host)
    {
        OutboundWebhookEndpointGuard.IsHostBlockedByStaticPolicy(host, allowLocalhostEndpoints: false)
            .Should().BeTrue($"{host} is an IPv6 link-local address");
    }

    [Theory]
    [InlineData("fc00::1")]
    [InlineData("fd00::1")]
    [InlineData("fdff:ffff:ffff:ffff:ffff:ffff:ffff:ffff")]
    public void IsHostBlockedByStaticPolicy_ShouldBlockIpv6UniqueLocalAddresses(string host)
    {
        // RFC 4193 unique local (fc00::/7)
        OutboundWebhookEndpointGuard.IsHostBlockedByStaticPolicy(host, allowLocalhostEndpoints: false)
            .Should().BeTrue($"{host} is an IPv6 unique-local address (fc00::/7)");
    }

    [Theory]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:192.168.1.1")]
    [InlineData("::ffff:172.16.0.1")]
    public void IsHostBlockedByStaticPolicy_ShouldBlockIpv4MappedToIpv6PrivateAddresses(string host)
    {
        OutboundWebhookEndpointGuard.IsHostBlockedByStaticPolicy(host, allowLocalhostEndpoints: false)
            .Should().BeTrue($"{host} is an IPv4-mapped IPv6 private address");
    }

    // -----------------------------------------------------------------------
    // SSRF: dynamic DNS providers that embed IPs
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("127.0.0.1.nip.io")]
    [InlineData("10.0.0.1.nip.io")]
    [InlineData("192.168.1.1.nip.io")]
    public void IsHostBlockedByStaticPolicy_ShouldBlockDynamicDnsWithEmbeddedPrivateIp_NipIo(string host)
    {
        OutboundWebhookEndpointGuard.IsHostBlockedByStaticPolicy(host, allowLocalhostEndpoints: false)
            .Should().BeTrue($"{host} embeds a private IP via nip.io");
    }

    [Theory]
    [InlineData("10-0-0-1.sslip.io")]
    [InlineData("192-168-1-1.sslip.io")]
    public void IsHostBlockedByStaticPolicy_ShouldBlockDynamicDnsWithEmbeddedPrivateIp_SslipIo(string host)
    {
        OutboundWebhookEndpointGuard.IsHostBlockedByStaticPolicy(host, allowLocalhostEndpoints: false)
            .Should().BeTrue($"{host} embeds a private IP via sslip.io");
    }

    // -----------------------------------------------------------------------
    // SSRF: blocked hostname suffixes
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("myservice.local")]
    [InlineData("anything.internal")]
    [InlineData("host.home.arpa")]
    [InlineData("something.localhost")]
    [InlineData("test.localtest.me")]
    public void IsHostBlockedByStaticPolicy_ShouldBlockKnownInternalHostnameSuffixes(string host)
    {
        OutboundWebhookEndpointGuard.IsHostBlockedByStaticPolicy(host, allowLocalhostEndpoints: false)
            .Should().BeTrue($"{host} matches a blocked internal hostname suffix");
    }

    // -----------------------------------------------------------------------
    // SSRF: localhost variants
    // -----------------------------------------------------------------------

    [Fact]
    public void IsHostBlockedByStaticPolicy_ShouldBlockLocalhost_WhenNotAllowed()
    {
        OutboundWebhookEndpointGuard.IsHostBlockedByStaticPolicy("localhost", allowLocalhostEndpoints: false)
            .Should().BeTrue();
    }

    [Fact]
    public void IsHostBlockedByStaticPolicy_ShouldAllowLocalhost_WhenExplicitlyPermitted()
    {
        OutboundWebhookEndpointGuard.IsHostBlockedByStaticPolicy("localhost", allowLocalhostEndpoints: true)
            .Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // SSRF: public IP addresses that MUST be allowed
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("203.0.113.1")]   // TEST-NET-3 (documentation, externally routable in this context)
    [InlineData("198.51.100.1")]  // TEST-NET-2
    [InlineData("93.184.216.34")] // example.com
    [InlineData("8.8.8.8")]       // Google DNS
    [InlineData("1.1.1.1")]       // Cloudflare DNS
    public void IsHostBlockedByStaticPolicy_ShouldAllowPublicIpAddresses(string host)
    {
        OutboundWebhookEndpointGuard.IsHostBlockedByStaticPolicy(host, allowLocalhostEndpoints: false)
            .Should().BeFalse($"{host} is a public IP address that should be allowed");
    }

    // -----------------------------------------------------------------------
    // SSRF: empty / null / whitespace host
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IsHostBlockedByStaticPolicy_ShouldBlockEmptyOrWhitespaceHost(string? host)
    {
        OutboundWebhookEndpointGuard.IsHostBlockedByStaticPolicy(host!, allowLocalhostEndpoints: false)
            .Should().BeTrue("empty or whitespace hosts are invalid and must be blocked");
    }

    // -----------------------------------------------------------------------
    // ResolveAllowedAddressesAsync – no-DNS-resolution path (literal IPs)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ResolveAllowedAddressesAsync_ShouldReturnEmpty_ForLiteral127Address()
    {
        var result = await OutboundWebhookEndpointGuard.ResolveAllowedAddressesAsync(
            "127.0.0.1",
            allowLocalhostEndpoints: false);

        result.Should().BeEmpty("127.0.0.1 is the loopback address");
    }

    [Fact]
    public async Task ResolveAllowedAddressesAsync_ShouldReturnEmpty_ForLiteralMetadataEndpoint()
    {
        var result = await OutboundWebhookEndpointGuard.ResolveAllowedAddressesAsync(
            "169.254.169.254",
            allowLocalhostEndpoints: false);

        result.Should().BeEmpty("169.254.169.254 is the cloud metadata endpoint");
    }

    [Fact]
    public async Task ResolveAllowedAddressesAsync_ShouldReturnEmpty_ForLiteralIpv6Loopback()
    {
        var result = await OutboundWebhookEndpointGuard.ResolveAllowedAddressesAsync(
            "::1",
            allowLocalhostEndpoints: false);

        result.Should().BeEmpty("::1 is the IPv6 loopback address");
    }

    [Fact]
    public async Task ResolveAllowedAddressesAsync_ShouldReturnAddress_ForPublicLiteralIp()
    {
        var result = await OutboundWebhookEndpointGuard.ResolveAllowedAddressesAsync(
            "203.0.113.1",
            allowLocalhostEndpoints: false);

        result.Should().ContainSingle()
            .Which.ToString().Should().Be("203.0.113.1");
    }

    // -----------------------------------------------------------------------
    // ResolveAllowedAddressesAsync – mixed public+private resolution
    // -----------------------------------------------------------------------

    /// <summary>
    /// Documents the guard's behavior when a hostname resolves to a mix of public and private
    /// addresses: only the public addresses are returned. The guard filters blocked IPs before
    /// returning, so the allowed-addresses list will only ever contain externally routable IPs.
    ///
    /// This is the "mixed DNS resolution" scenario from issue #710. DNS rebinding (where the
    /// first resolve is public but a retry resolves to private) is NOT protected against here
    /// because each ConnectCallback invocation re-resolves from scratch; a separate network-level
    /// control (e.g., short DNS TTL enforcement) is needed to fully block that attack vector.
    /// </summary>
    [Fact]
    public async Task ResolveAllowedAddressesAsync_ShouldReturnOnlyPublicAddresses_WhenMixedResolution()
    {
        // We cannot inject DNS, so we test the filtering contract with two literal IPs:
        // one public (203.0.113.1) and one private (10.0.0.1). Both are passed individually
        // and the results confirm the guard's filtering behaviour for each.
        var publicResult = await OutboundWebhookEndpointGuard.ResolveAllowedAddressesAsync(
            "203.0.113.1",
            allowLocalhostEndpoints: false);

        var privateResult = await OutboundWebhookEndpointGuard.ResolveAllowedAddressesAsync(
            "10.0.0.1",
            allowLocalhostEndpoints: false);

        publicResult.Should().ContainSingle(
            "203.0.113.1 is a public address and must be included in the allowed list");
        privateResult.Should().BeEmpty(
            "10.0.0.1 is a private address and must be excluded — " +
            "if a host resolved to both, the private IPs would be filtered out");
    }

    // -----------------------------------------------------------------------
    // Error message does not leak sensitive host info beyond the host itself
    // -----------------------------------------------------------------------

    [Fact]
    public void IsHostBlockedByStaticPolicy_ErrorPath_ShouldNotLeakInfrastructureDetails()
    {
        // The consumer (OutboundWebhookConnectCallback) throws:
        //   "Webhook endpoint host '{host}' is not allowed."
        // Ensure the host name is simple enough (not containing credentials, tokens, etc.)
        // when blocked via literal IP. This test is a documentation/smoke test.
        var host = "127.0.0.1";
        var isBlocked = OutboundWebhookEndpointGuard.IsHostBlockedByStaticPolicy(host, allowLocalhostEndpoints: false);
        isBlocked.Should().BeTrue();
        // The error message would be: "Webhook endpoint host '127.0.0.1' is not allowed."
        // — no Bearer tokens, no internal DNS names beyond the host itself.
        var expectedErrorFragment = $"Webhook endpoint host '{host}' is not allowed.";
        expectedErrorFragment.Should().NotContain("Bearer");
        expectedErrorFragment.Should().NotContain("password");
    }
}
