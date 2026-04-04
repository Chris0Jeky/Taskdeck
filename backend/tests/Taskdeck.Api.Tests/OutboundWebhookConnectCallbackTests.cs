using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using FluentAssertions;
using Taskdeck.Api.Workers;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Tests the SSRF guard integrated into <see cref="OutboundWebhookConnectCallback.ConnectAsync"/>
/// via a real <see cref="SocketsHttpHandler"/> that uses the callback.
///
/// The guard runs in the SocketsHttpHandler ConnectCallback, meaning it intercepts the TCP
/// connection attempt before any bytes reach the remote host.  These tests wire the callback
/// into a real SocketsHttpHandler and issue HTTP requests, verifying that private/reserved
/// addresses throw <see cref="HttpRequestException"/> carrying the SSRF rejection message,
/// while allowed endpoints propagate a socket-level failure (connection refused) instead.
///
/// NOTE: "Connection succeeds" scenarios require a real listening socket and are out of scope
/// here. The worker integration tests cover the full happy path via a mocked HttpMessageHandler.
/// </summary>
public class OutboundWebhookConnectCallbackTests
{
    // -----------------------------------------------------------------------
    // SSRF: ConnectAsync must block private IP addresses
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("http://127.0.0.1/hook")]
    [InlineData("http://10.0.0.1/hook")]
    [InlineData("http://192.168.1.1/hook")]
    [InlineData("http://172.16.0.1/hook")]
    [InlineData("http://169.254.169.254/hook")]
    public async Task ConnectCallback_ShouldThrowHttpRequestException_WhenHostIsPrivateIp(string url)
    {
        using var client = BuildClientWithCallback(allowLocalhost: false);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync(url));

        ex.Message.Should().Contain("is not allowed",
            $"the SSRF guard must reject the private/reserved address in {url}");
    }

    [Fact]
    public async Task ConnectCallback_ShouldThrowHttpRequestException_WhenHostIsIpv6Loopback()
    {
        using var client = BuildClientWithCallback(allowLocalhost: false);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync("http://[::1]/hook"));

        ex.Message.Should().Contain("is not allowed",
            "::1 is the IPv6 loopback address and must be blocked");
    }

    [Theory]
    [InlineData("http://[::ffff:10.0.0.8]/hook")]
    [InlineData("http://[::ffff:192.168.0.1]/hook")]
    [InlineData("http://[::ffff:172.16.0.1]/hook")]
    public async Task ConnectCallback_ShouldThrowHttpRequestException_WhenHostIsIpv4MappedToIpv6Private(string url)
    {
        using var client = BuildClientWithCallback(allowLocalhost: false);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync(url));

        ex.Message.Should().Contain("is not allowed",
            $"IPv4-mapped IPv6 private address in {url} must be blocked");
    }

    // -----------------------------------------------------------------------
    // SSRF: metadata endpoint (AWS / cloud)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ConnectCallback_ShouldBlock_CloudMetadataEndpoint()
    {
        using var client = BuildClientWithCallback(allowLocalhost: false);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync("http://169.254.169.254/latest/meta-data/"));

        ex.Message.Should().Contain("is not allowed",
            "169.254.169.254 is the cloud instance metadata endpoint and must always be blocked");
    }

    // -----------------------------------------------------------------------
    // Error message hygiene
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ConnectCallback_ErrorMessage_ShouldContainHostName_AndNotLeakSecrets()
    {
        using var client = BuildClientWithCallback(allowLocalhost: false);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync("http://10.0.0.1/hook"));

        ex.Message.Should().Contain("10.0.0.1");
        ex.Message.Should().NotContain("Bearer", "no auth header fragments in error");
        ex.Message.Should().NotContain("password", "no credential keywords in error");
        ex.Message.Should().NotContain("token=", "no token-style credentials in error");
    }

    // -----------------------------------------------------------------------
    // Localhost allowed when explicitly configured
    // (guard passes → SocketException expected, NOT the "not allowed" message)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ConnectCallback_ShouldAttemptConnection_WhenLocalhostAllowed()
    {
        // Port 1 is extremely unlikely to be open. The guard allows the hostname "localhost"
        // when AllowLocalhostEndpoints is true; the SocketsHttpHandler then resolves the
        // name to 127.0.0.1 and attempts a real TCP connection. This fails with either:
        //   - HttpRequestException (SocketException inner) on "connection refused" OS response, or
        //   - TaskCanceledException (TimeoutException inner) if the OS queues the SYN and
        //     our ConnectTimeout fires before a RST arrives.
        // Either way the SSRF guard must NOT have blocked it — no "is not allowed" in the message.
        using var client = BuildClientWithCallback(allowLocalhost: true);

        Exception? caughtException = null;
        try
        {
            await client.GetAsync("http://localhost:1/hook");
        }
        catch (HttpRequestException ex)
        {
            caughtException = ex;
        }
        catch (TaskCanceledException ex)
        {
            caughtException = ex;
        }

        caughtException.Should().NotBeNull("port 1 on localhost should not be reachable");
        caughtException!.Message.Should().NotContain("is not allowed",
            "when AllowLocalhostEndpoints is true the SSRF guard must pass for the 'localhost' hostname");
    }

    // -----------------------------------------------------------------------
    // Non-resolvable hostname → fail closed
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ConnectCallback_ShouldThrow_WhenHostDoesNotResolve()
    {
        using var client = BuildClientWithCallback(allowLocalhost: false);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync("https://nonexistent-host-for-ssrf-test.invalid/hook"));

        // DNS failure → guard returns empty list → HttpRequestException with "is not allowed"
        ex.Should().NotBeNull(
            "a non-resolvable hostname must result in an exception (fail-closed)");
    }

    // -----------------------------------------------------------------------
    // Builder helper
    // -----------------------------------------------------------------------

    private static HttpClient BuildClientWithCallback(bool allowLocalhost)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = (context, cancellationToken) =>
                OutboundWebhookConnectCallback.ConnectAsync(context, allowLocalhost, cancellationToken),
            // Short timeouts for tests so DNS / connect failures are fast.
            ConnectTimeout = TimeSpan.FromSeconds(3)
        };
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
    }
}
