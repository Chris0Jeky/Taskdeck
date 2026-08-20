using FluentAssertions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class SsrfProtectionServiceTests
{
    // -----------------------------------------------------------------------
    // ValidateUrl — scheme validation
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://example.com")]
    public void ValidateUrl_ShouldAllowHttpAndHttpsSchemes(string url)
    {
        var result = SsrfProtectionService.ValidateUrl(url);
        result.IsAllowed.Should().BeTrue($"{url} uses an allowed scheme");
    }

    [Theory]
    [InlineData("ftp://example.com")]
    [InlineData("file:///etc/passwd")]
    [InlineData("gopher://evil.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<h1>hi</h1>")]
    public void ValidateUrl_ShouldBlockNonHttpSchemes(string url)
    {
        var result = SsrfProtectionService.ValidateUrl(url);
        result.IsAllowed.Should().BeFalse($"{url} uses a disallowed scheme");
        result.ErrorMessage.Should().Contain("http or https");
    }

    // -----------------------------------------------------------------------
    // ValidateUrl — null / empty / whitespace
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateUrl_ShouldBlockNullOrEmptyUrl(string? url)
    {
        var result = SsrfProtectionService.ValidateUrl(url);
        result.IsAllowed.Should().BeFalse();
        result.ErrorMessage.Should().Contain("required");
    }

    // -----------------------------------------------------------------------
    // ValidateUrl — malformed URLs
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("://missing-scheme")]
    [InlineData("http://")]
    public void ValidateUrl_ShouldBlockMalformedUrls(string url)
    {
        var result = SsrfProtectionService.ValidateUrl(url);
        result.IsAllowed.Should().BeFalse($"'{url}' is malformed");
    }

    // -----------------------------------------------------------------------
    // ValidateUrl — embedded credentials
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("https://user:pass@example.com")]
    [InlineData("https://admin@example.com")]
    public void ValidateUrl_ShouldBlockUrlsWithEmbeddedCredentials(string url)
    {
        var result = SsrfProtectionService.ValidateUrl(url);
        result.IsAllowed.Should().BeFalse($"'{url}' contains embedded credentials");
        result.ErrorMessage.Should().Contain("credentials");
    }

    // -----------------------------------------------------------------------
    // ValidateUrl — private IPv4 ranges
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("https://127.0.0.1/api")]
    [InlineData("https://127.1.2.3/webhook")]
    [InlineData("https://127.255.255.255")]
    public void ValidateUrl_ShouldBlockLoopbackIpv4(string url)
    {
        var result = SsrfProtectionService.ValidateUrl(url);
        result.IsAllowed.Should().BeFalse($"{url} targets loopback (127.0.0.0/8)");
    }

    [Theory]
    [InlineData("https://10.0.0.1")]
    [InlineData("https://10.128.0.1/path")]
    [InlineData("https://10.255.255.255")]
    public void ValidateUrl_ShouldBlockClassA_10_PrivateRange(string url)
    {
        var result = SsrfProtectionService.ValidateUrl(url);
        result.IsAllowed.Should().BeFalse($"{url} targets 10.0.0.0/8 private range");
    }

    [Theory]
    [InlineData("https://172.16.0.1")]
    [InlineData("https://172.20.5.10/api")]
    [InlineData("https://172.31.255.255")]
    public void ValidateUrl_ShouldBlock_172_16_PrivateRange(string url)
    {
        var result = SsrfProtectionService.ValidateUrl(url);
        result.IsAllowed.Should().BeFalse($"{url} targets 172.16.0.0/12 private range");
    }

    [Theory]
    [InlineData("https://192.168.0.1")]
    [InlineData("https://192.168.100.5/hook")]
    [InlineData("https://192.168.255.254")]
    public void ValidateUrl_ShouldBlock_192_168_PrivateRange(string url)
    {
        var result = SsrfProtectionService.ValidateUrl(url);
        result.IsAllowed.Should().BeFalse($"{url} targets 192.168.0.0/16 private range");
    }

    [Theory]
    [InlineData("https://0.0.0.0")]
    public void ValidateUrl_ShouldBlockUnspecifiedAddress(string url)
    {
        var result = SsrfProtectionService.ValidateUrl(url);
        result.IsAllowed.Should().BeFalse($"{url} targets the unspecified address");
    }

    // -----------------------------------------------------------------------
    // ValidateUrl — link-local and cloud metadata IP
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("http://169.254.169.254")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("https://169.254.169.254/computeMetadata/v1/")]
    public void ValidateUrl_ShouldBlockCloudMetadataIp(string url)
    {
        var result = SsrfProtectionService.ValidateUrl(url);
        result.IsAllowed.Should().BeFalse($"{url} targets cloud metadata endpoint (169.254.169.254)");
    }

    // -----------------------------------------------------------------------
    // ValidateUrl — private IPv6 ranges
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("https://[::1]")]
    [InlineData("https://[::1]/api")]
    public void ValidateUrl_ShouldBlockIpv6Loopback(string url)
    {
        var result = SsrfProtectionService.ValidateUrl(url);
        result.IsAllowed.Should().BeFalse($"{url} targets IPv6 loopback (::1)");
    }

    [Theory]
    [InlineData("https://[fc00::1]")]
    [InlineData("https://[fd00::1]")]
    [InlineData("https://[fdff:ffff:ffff:ffff:ffff:ffff:ffff:ffff]")]
    public void ValidateUrl_ShouldBlockIpv6UniqueLocal(string url)
    {
        var result = SsrfProtectionService.ValidateUrl(url);
        result.IsAllowed.Should().BeFalse($"{url} targets IPv6 unique-local (fc00::/7)");
    }

    [Theory]
    [InlineData("https://[fe80::1]")]
    [InlineData("https://[fe80::abcd:ef01]")]
    public void ValidateUrl_ShouldBlockIpv6LinkLocal(string url)
    {
        var result = SsrfProtectionService.ValidateUrl(url);
        result.IsAllowed.Should().BeFalse($"{url} targets IPv6 link-local (fe80::/10)");
    }

    [Theory]
    [InlineData("https://[::ffff:127.0.0.1]")]
    [InlineData("https://[::ffff:10.0.0.1]")]
    [InlineData("https://[::ffff:192.168.1.1]")]
    [InlineData("https://[::ffff:172.16.0.1]")]
    public void ValidateUrl_ShouldBlockIpv4MappedToIpv6(string url)
    {
        var result = SsrfProtectionService.ValidateUrl(url);
        result.IsAllowed.Should().BeFalse($"{url} targets IPv4-mapped IPv6 private address");
    }

    // -----------------------------------------------------------------------
    // ValidateUrl — cloud metadata hostnames
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("https://metadata.google.internal")]
    [InlineData("https://metadata.google.internal/computeMetadata/v1/")]
    public void ValidateUrl_ShouldBlockGoogleMetadataHostname(string url)
    {
        var result = SsrfProtectionService.ValidateUrl(url);
        result.IsAllowed.Should().BeFalse($"{url} targets Google Cloud metadata endpoint");
    }

    [Theory]
    [InlineData("https://metadata.goog")]
    [InlineData("https://metadata.goog/computeMetadata/v1/")]
    public void ValidateUrl_ShouldBlockGoogleMetadataGoogHostname(string url)
    {
        var result = SsrfProtectionService.ValidateUrl(url);
        result.IsAllowed.Should().BeFalse($"{url} targets Google Cloud metadata.goog endpoint");
    }

    [Theory]
    [InlineData("https://100.100.100.200")]
    [InlineData("https://100.100.100.200/latest/meta-data/")]
    public void ValidateUrl_ShouldBlockAlibabaCloudMetadata(string url)
    {
        var result = SsrfProtectionService.ValidateUrl(url);
        result.IsAllowed.Should().BeFalse($"{url} targets Alibaba Cloud metadata endpoint");
    }

    // -----------------------------------------------------------------------
    // ValidateUrl — localhost handling
    // -----------------------------------------------------------------------

    [Fact]
    public void ValidateUrl_ShouldBlockLocalhost_WhenNotAllowed()
    {
        var result = SsrfProtectionService.ValidateUrl("https://localhost/api", allowLocalhostEndpoints: false);
        result.IsAllowed.Should().BeFalse();
    }

    [Fact]
    public void ValidateUrl_ShouldAllowLocalhost_WhenExplicitlyPermitted()
    {
        var result = SsrfProtectionService.ValidateUrl("https://localhost/api", allowLocalhostEndpoints: true);
        result.IsAllowed.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // ValidateUrl — blocked hostname suffixes
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("https://myservice.local")]
    [InlineData("https://anything.internal")]
    [InlineData("https://host.home.arpa")]
    [InlineData("https://something.localhost")]
    [InlineData("https://test.localtest.me")]
    public void ValidateUrl_ShouldBlockInternalHostnameSuffixes(string url)
    {
        var result = SsrfProtectionService.ValidateUrl(url);
        result.IsAllowed.Should().BeFalse($"{url} matches a blocked internal hostname suffix");
    }

    // -----------------------------------------------------------------------
    // ValidateUrl — dynamic DNS with embedded private IPs
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("https://127.0.0.1.nip.io")]
    [InlineData("https://10.0.0.1.nip.io")]
    [InlineData("https://192.168.1.1.nip.io")]
    [InlineData("https://10-0-0-1.sslip.io")]
    public void ValidateUrl_ShouldBlockDynamicDnsWithEmbeddedPrivateIp(string url)
    {
        var result = SsrfProtectionService.ValidateUrl(url);
        result.IsAllowed.Should().BeFalse($"{url} uses dynamic DNS to embed a private IP");
    }

    // -----------------------------------------------------------------------
    // ValidateUrl — public URLs that MUST be allowed
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("https://example.com/webhook")]
    [InlineData("https://api.openai.com/v1/chat/completions")]
    [InlineData("https://hooks.slack.com/services/T00/B00/xxx")]
    [InlineData("https://93.184.216.34/api")]
    [InlineData("https://8.8.8.8")]
    [InlineData("https://1.1.1.1")]
    public void ValidateUrl_ShouldAllowPublicUrls(string url)
    {
        var result = SsrfProtectionService.ValidateUrl(url);
        result.IsAllowed.Should().BeTrue($"{url} is a public URL that should be allowed");
        result.ParsedUri.Should().NotBeNull();
    }

    // -----------------------------------------------------------------------
    // ValidateUrl — SSRF bypass attempts
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("https://127.0.0.1.nip.io")]     // DNS rebinding via nip.io
    [InlineData("https://10-0-0-1.sslip.io")]     // DNS rebinding via sslip.io
    public void ValidateUrl_ShouldBlockDnsRebindingAttempts(string url)
    {
        var result = SsrfProtectionService.ValidateUrl(url);
        result.IsAllowed.Should().BeFalse($"{url} is a DNS rebinding bypass attempt");
    }

    [Theory]
    [InlineData("https://[::ffff:7f00:1]")]  // IPv4-mapped IPv6 for 127.0.0.1
    [InlineData("https://[::ffff:a00:1]")]   // IPv4-mapped IPv6 for 10.0.0.1
    public void ValidateUrl_ShouldBlockIpv4MappedIpv6Bypasses(string url)
    {
        var result = SsrfProtectionService.ValidateUrl(url);
        result.IsAllowed.Should().BeFalse($"{url} uses IPv4-mapped IPv6 to bypass filters");
    }

    // -----------------------------------------------------------------------
    // ValidateUrl — decimal/hex/octal IP notation bypass attempts
    // .NET's Uri normalizes these to dotted-decimal, so the IP check catches them
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("https://2130706433")]         // decimal for 127.0.0.1
    [InlineData("https://0x7f000001")]         // hex for 127.0.0.1
    [InlineData("https://0177.0.0.1")]         // octal for 127.0.0.1
    [InlineData("https://127.1")]              // short form for 127.0.0.1
    [InlineData("https://127.0.1")]            // short form for 127.0.0.1
    public void ValidateUrl_ShouldBlockDecimalHexOctalIpBypasses(string url)
    {
        var result = SsrfProtectionService.ValidateUrl(url);
        result.IsAllowed.Should().BeFalse(
            $"{url} uses decimal/hex/octal notation to disguise a private IP — " +
            ".NET Uri normalizes it, and the IP check should catch it");
    }

    [Theory]
    [InlineData("https://167772161")]          // decimal for 10.0.0.1
    [InlineData("https://0xA000001")]          // hex for 10.0.0.1
    [InlineData("https://0xC0A80101")]         // hex for 192.168.1.1
    public void ValidateUrl_ShouldBlockDecimalHexPrivateIpBypasses(string url)
    {
        var result = SsrfProtectionService.ValidateUrl(url);
        result.IsAllowed.Should().BeFalse(
            $"{url} uses non-standard notation for a private IP");
    }

    // -----------------------------------------------------------------------
    // ValidateLlmProviderUrl — HTTPS enforcement
    // -----------------------------------------------------------------------

    [Fact]
    public void ValidateLlmProviderUrl_ShouldRequireHttps()
    {
        var result = SsrfProtectionService.ValidateLlmProviderUrl("http://api.openai.com/v1");
        result.IsAllowed.Should().BeFalse("LLM provider URLs must use HTTPS");
        result.ErrorMessage.Should().Contain("HTTPS");
    }

    [Fact]
    public void ValidateLlmProviderUrl_ShouldAllowHttps()
    {
        var result = SsrfProtectionService.ValidateLlmProviderUrl("https://api.openai.com/v1");
        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void ValidateLlmProviderUrl_ShouldAllowHttpForLocalhost_WhenPermitted()
    {
        var result = SsrfProtectionService.ValidateLlmProviderUrl(
            "http://localhost:11434/v1",
            allowLocalhostEndpoints: true);
        result.IsAllowed.Should().BeTrue("localhost with HTTP is allowed when explicitly permitted (e.g., for Ollama)");
    }

    [Fact]
    public void ValidateLlmProviderUrl_ShouldBlockHttpForLocalhost_WhenNotPermitted()
    {
        var result = SsrfProtectionService.ValidateLlmProviderUrl("http://localhost:11434/v1");
        result.IsAllowed.Should().BeFalse("localhost not allowed by default");
    }

    [Fact]
    public void ValidateLlmProviderUrl_ShouldBlockPrivateIp()
    {
        var result = SsrfProtectionService.ValidateLlmProviderUrl("https://10.0.0.5/v1/chat/completions");
        result.IsAllowed.Should().BeFalse("private IPs should be blocked for LLM providers");
    }

    [Fact]
    public void ValidateLlmProviderUrl_ShouldBlockCloudMetadataEndpoint()
    {
        var result = SsrfProtectionService.ValidateLlmProviderUrl("https://metadata.google.internal/computeMetadata/v1/");
        result.IsAllowed.Should().BeFalse("cloud metadata endpoint should be blocked for LLM providers");
    }

    [Theory]
    [InlineData("https://api.openai.com/v1")]
    [InlineData("https://api.groq.com/openai/v1")]
    public void ValidateLlmProviderUrl_ShouldAllowLegitimateProviderUrls(string url)
    {
        var result = SsrfProtectionService.ValidateLlmProviderUrl(url);
        result.IsAllowed.Should().BeTrue($"{url} is a legitimate LLM provider URL");
    }

    // -----------------------------------------------------------------------
    // ValidateUrlWithDnsAsync — basic async validation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ValidateUrlWithDnsAsync_ShouldBlockPrivateIp()
    {
        var result = await SsrfProtectionService.ValidateUrlWithDnsAsync("https://10.0.0.1/api");
        result.IsAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateUrlWithDnsAsync_ShouldBlockNull()
    {
        var result = await SsrfProtectionService.ValidateUrlWithDnsAsync(null);
        result.IsAllowed.Should().BeFalse();
        result.ErrorMessage.Should().Contain("required");
    }

    [Fact]
    public async Task ValidateUrlWithDnsAsync_ShouldBlockNonExistentHost()
    {
        var result = await SsrfProtectionService.ValidateUrlWithDnsAsync(
            "https://nonexistent-ssrf-test-host.invalid/api");
        result.IsAllowed.Should().BeFalse("non-existent hosts should fail closed");
    }

    // -----------------------------------------------------------------------
    // Error messages — clarity
    // -----------------------------------------------------------------------

    [Fact]
    public void ValidateUrl_ErrorMessage_ShouldBeClearForPrivateIp()
    {
        var result = SsrfProtectionService.ValidateUrl("https://192.168.1.1/api");
        result.ErrorMessage.Should().Contain("not allowed");
    }

    [Fact]
    public void ValidateUrl_ErrorMessage_ShouldBeClearForCloudMetadata()
    {
        var result = SsrfProtectionService.ValidateUrl("https://metadata.goog/api");
        result.ErrorMessage.Should().Contain("not allowed");
    }

    [Fact]
    public void ValidateUrl_ErrorMessage_ShouldNotLeakInternalDetails()
    {
        var result = SsrfProtectionService.ValidateUrl("https://10.0.0.1/api");
        result.ErrorMessage.Should().NotContain("Bearer");
        result.ErrorMessage.Should().NotContain("password");
        result.ErrorMessage.Should().NotContain("token");
    }
}
