using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Taskdeck.Api.Extensions;
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
/// A loopback listener also proves the registered direct-origin path when development explicitly
/// permits localhost, while a throwing proxy canary proves that the protected pipeline never
/// consults a configured proxy.
/// </summary>
public class OutboundWebhookConnectCallbackTests
{
    private const string OutboundWebhookClientName = "OutboundWebhookDelivery";

    [Fact]
    public void AddTaskdeckWorkers_ShouldDisableProxyAndRetainOriginGuards_OnFactoryPipeline()
    {
        using var serviceProvider = BuildWebhookServiceProvider("Production");

        var pipeline = serviceProvider
            .GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(OutboundWebhookClientName);

        ProxySafeHttpHandlerTestHarness.AssertProxySafeOriginHandler(pipeline);
    }

    [Theory]
    [InlineData("http://127.0.0.1/protected")]
    [InlineData("http://10.0.0.1/protected")]
    [InlineData("http://169.254.169.254/protected")]
    public async Task AddTaskdeckWorkers_ShouldRejectBlockedOriginWithoutConsultingHostileProxy(
        string blockedOrigin)
    {
        using var serviceProvider = BuildWebhookServiceProvider("Production");
        var pipeline = serviceProvider
            .GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(OutboundWebhookClientName);

        await ProxySafeHttpHandlerTestHarness.AssertBlockedOriginIgnoresProxyAsync(
            pipeline,
            blockedOrigin);
    }

    [Fact]
    public async Task AddTaskdeckWorkers_ShouldReachAllowedDirectOriginWithoutConsultingHostileProxy()
    {
        using var serviceProvider = BuildWebhookServiceProvider("Development");
        var pipeline = serviceProvider
            .GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(OutboundWebhookClientName);

        await ProxySafeHttpHandlerTestHarness.AssertDirectOriginIgnoresProxyAsync(pipeline);
    }

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

    private static ServiceProvider BuildWebhookServiceProvider(string environmentName)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder().Build();
        services.AddTaskdeckWorkers(
            configuration,
            new TestHostEnvironment(environmentName));
        return services.BuildServiceProvider();
    }

    private static HttpClient BuildClientWithCallback(bool allowLocalhost)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
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

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "Taskdeck.Api.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = environmentName;
    }
}

internal static class ProxySafeHttpHandlerTestHarness
{
    private const string SensitiveMarker = "must-not-reach-a-proxy";

    internal static void AssertProxySafeOriginHandler(HttpMessageHandler pipeline)
    {
        var primaryHandler = GetPrimaryHandler(pipeline);

        primaryHandler.UseProxy.Should().BeFalse(
            "origin validation must not be redirected to an ambient or configured proxy");
        primaryHandler.AllowAutoRedirect.Should().BeFalse(
            "redirects must not move a validated request to an unvalidated origin");
        primaryHandler.ConnectCallback.Should().NotBeNull(
            "the configured origin must retain DNS and IP validation at connect time");
    }

    internal static async Task AssertBlockedOriginIgnoresProxyAsync(
        HttpMessageHandler pipeline,
        string blockedOrigin)
    {
        AssertProxySafeOriginHandler(pipeline);
        var primaryHandler = GetPrimaryHandler(pipeline);
        var hostileProxy = new ThrowingWebProxy();
        primaryHandler.Proxy = hostileProxy;

        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var invoker = new HttpMessageInvoker(pipeline, disposeHandler: false);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{blockedOrigin}?marker={SensitiveMarker}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SensitiveMarker);
        request.Content = new StringContent(SensitiveMarker);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => invoker.SendAsync(request, cancellationSource.Token));

        exception.Message.Should().Contain(new Uri(blockedOrigin).Host);
        exception.Message.Should().Contain("is not allowed");
        exception.Message.Should().NotContain(SensitiveMarker);
        hostileProxy.InvocationCount.Should().Be(0,
            "UseProxy=false must keep the proxy from seeing the origin or protected request content");
    }

    internal static async Task AssertDirectOriginIgnoresProxyAsync(HttpMessageHandler pipeline)
    {
        AssertProxySafeOriginHandler(pipeline);
        var primaryHandler = GetPrimaryHandler(pipeline);
        var hostileProxy = new ThrowingWebProxy();
        primaryHandler.Proxy = hostileProxy;

        var listener = new TcpListener(IPAddress.Loopback, 0);
        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Task<string>? serverTask = null;
        try
        {
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            serverTask = ReceiveSingleRequestAsync(listener, cancellationSource.Token);

            using var invoker = new HttpMessageInvoker(pipeline, disposeHandler: false);
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"http://localhost:{port}/direct-origin");
            using var response = await invoker.SendAsync(request, cancellationSource.Token);
            var receivedRequest = await serverTask;

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            receivedRequest.Should().Contain("GET /direct-origin HTTP/1.1");
            hostileProxy.InvocationCount.Should().Be(0,
                "UseProxy=false must send an allowed request directly to its configured origin");
        }
        finally
        {
            cancellationSource.Cancel();
            listener.Stop();
            if (serverTask is { IsCompleted: false })
            {
                try
                {
                    await serverTask;
                }
                catch (OperationCanceledException)
                {
                }
                catch (SocketException)
                {
                }
            }
        }
    }

    private static SocketsHttpHandler GetPrimaryHandler(HttpMessageHandler pipeline)
    {
        var current = pipeline;
        while (current is DelegatingHandler delegatingHandler)
        {
            delegatingHandler.InnerHandler.Should().NotBeNull();
            current = delegatingHandler.InnerHandler!;
        }

        current.Should().BeOfType<SocketsHttpHandler>();
        return (SocketsHttpHandler)current;
    }

    private static async Task<string> ReceiveSingleRequestAsync(
        TcpListener listener,
        CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        using var stream = client.GetStream();
        using var received = new MemoryStream();
        var buffer = new byte[1024];

        while (received.Length < 16 * 1024)
        {
            var count = await stream.ReadAsync(buffer, cancellationToken);
            if (count == 0)
            {
                break;
            }

            received.Write(buffer, 0, count);
            var requestText = Encoding.ASCII.GetString(received.GetBuffer(), 0, (int)received.Length);
            if (requestText.Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                var response = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 204 No Content\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(response, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                return requestText;
            }
        }

        throw new InvalidOperationException("Direct-origin canary did not receive a complete HTTP request.");
    }

    private sealed class ThrowingWebProxy : IWebProxy
    {
        private int _invocationCount;

        public int InvocationCount => Volatile.Read(ref _invocationCount);

        public ICredentials? Credentials { get; set; }

        public Uri GetProxy(Uri destination) => RejectProxyConsultation();

        public bool IsBypassed(Uri host)
        {
            RejectProxyConsultation();
            return false;
        }

        private Uri RejectProxyConsultation()
        {
            Interlocked.Increment(ref _invocationCount);
            throw new InvalidOperationException("The hostile proxy must not be consulted.");
        }
    }
}
