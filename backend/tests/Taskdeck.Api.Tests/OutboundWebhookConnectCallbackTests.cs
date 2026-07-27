using System.Globalization;
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
using Microsoft.Extensions.Logging;
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

    [Fact]
    public async Task AddTaskdeckWorkers_ShouldSuppressProtectedRequestLogging()
    {
        var loggerProvider = new RecordingHttpLoggerProvider();
        using var serviceProvider = BuildWebhookServiceProvider("Production", loggerProvider);
        var pipeline = serviceProvider
            .GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(OutboundWebhookClientName);

        await ProxySafeHttpHandlerTestHarness.AssertBlockedOriginIgnoresProxyAsync(
            pipeline,
            "http://127.0.0.1/protected");

        loggerProvider.Messages.Should().NotContain(
            message => message.Contains(ProxySafeHttpHandlerTestHarness.SensitiveMarker, StringComparison.Ordinal),
            "protected webhook query/header/body markers must not reach default IHttpClientFactory logs");
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

    private static ServiceProvider BuildWebhookServiceProvider(
        string environmentName,
        ILoggerProvider? loggerProvider = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            if (loggerProvider is not null)
            {
                builder.AddProvider(loggerProvider);
            }
        });
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
    internal const string SensitiveMarker = "must-not-reach-a-proxy";

    internal static void AssertProxySafeOriginHandler(HttpMessageHandler pipeline)
    {
        var primaryHandler = GetPrimaryHandler(pipeline);

        primaryHandler.UseProxy.Should().BeFalse(
            "origin validation must not be redirected to an ambient or configured proxy");
        primaryHandler.AllowAutoRedirect.Should().BeFalse(
            "redirects must not move a validated request to an unvalidated origin");
        primaryHandler.ConnectCallback.Should().NotBeNull(
            "the configured origin must retain DNS and IP validation at connect time");
        primaryHandler.ActivityHeadersPropagator.Should().BeNull(
            "protected destinations must not receive ambient distributed-trace headers");
        primaryHandler.MeterFactory.Should().BeOfType<ProtectedOutboundMeterFactory>(
            "protected HTTP metrics must use the scope Taskdeck excludes from its configured exporters");
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

        var requestPath = $"/direct-origin-{Guid.NewGuid():N}";
        await using var server = new SingleRequestLoopbackServer(HttpStatusCode.NoContent);
        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        using var invoker = new HttpMessageInvoker(pipeline, disposeHandler: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, server.BuildUri(requestPath));
        using var response = await invoker.SendAsync(request, cancellationSource.Token);
        var receivedRequest = await server.ReceivedRequest;

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        receivedRequest.Should().Contain($"GET {requestPath} HTTP/1.1");
        hostileProxy.InvocationCount.Should().Be(0,
            "UseProxy=false must send an allowed request directly to its configured origin");
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

internal sealed class SingleRequestLoopbackServer : IAsyncDisposable
{
    private const int MaximumRequestBytes = 64 * 1024;
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cancellationSource = new(TimeSpan.FromSeconds(10));
    private readonly Task<CapturedRequest> _capturedRequest;

    internal SingleRequestLoopbackServer(
        HttpStatusCode responseStatus = HttpStatusCode.OK,
        string responseBody = "")
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _capturedRequest = ReceiveSingleRequestAsync(
            responseStatus,
            responseBody,
            _cancellationSource.Token);
        ReceivedRequest = SelectRawRequestAsync(_capturedRequest);
        ReceivedBody = SelectBodyAsync(_capturedRequest);
    }

    internal int Port { get; }

    internal Task<string> ReceivedRequest { get; }

    internal Task<string> ReceivedBody { get; }

    internal Uri BuildUri(string path)
    {
        var normalizedPath = path.StartsWith('/') ? path : $"/{path}";
        return new Uri($"http://localhost:{Port}{normalizedPath}");
    }

    public async ValueTask DisposeAsync()
    {
        _cancellationSource.Cancel();
        _listener.Stop();

        if (!_capturedRequest.IsCompleted)
        {
            try
            {
                await _capturedRequest;
            }
            catch (OperationCanceledException)
            {
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        _cancellationSource.Dispose();
    }

    private async Task<CapturedRequest> ReceiveSingleRequestAsync(
        HttpStatusCode responseStatus,
        string responseBody,
        CancellationToken cancellationToken)
    {
        using var client = await _listener.AcceptTcpClientAsync(cancellationToken);
        using var stream = client.GetStream();
        using var received = new MemoryStream();
        var buffer = new byte[2048];
        var bodyStart = -1;
        int? contentLength = null;
        var isChunked = false;

        while (received.Length < MaximumRequestBytes)
        {
            var count = await stream.ReadAsync(buffer, cancellationToken);
            if (count == 0)
            {
                break;
            }

            received.Write(buffer, 0, count);
            var requestBytes = received.GetBuffer();
            var requestLength = checked((int)received.Length);
            if (bodyStart < 0)
            {
                var requestText = Encoding.ASCII.GetString(requestBytes, 0, requestLength);
                var headerEnd = requestText.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (headerEnd < 0)
                {
                    continue;
                }

                var headers = requestText[..headerEnd];
                bodyStart = headerEnd + 4;
                contentLength = ParseContentLength(headers);
                isChunked = HasChunkedTransferEncoding(headers);
            }

            if (TryCaptureRequest(
                    requestBytes,
                    requestLength,
                    bodyStart,
                    contentLength,
                    isChunked,
                    out var capturedRequest))
            {
                await WriteResponseAsync(stream, responseStatus, responseBody, cancellationToken);
                return capturedRequest;
            }
        }

        throw new InvalidOperationException("Loopback canary did not receive a complete HTTP request.");
    }

    private static int? ParseContentLength(string headers)
    {
        foreach (var line in headers.Split("\r\n", StringSplitOptions.None))
        {
            const string prefix = "Content-Length:";
            if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!int.TryParse(
                    line[prefix.Length..].Trim(),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var contentLength) ||
                contentLength < 0 ||
                contentLength > MaximumRequestBytes)
            {
                throw new InvalidOperationException("Loopback request Content-Length is invalid.");
            }

            return contentLength;
        }

        return null;
    }

    private static bool HasChunkedTransferEncoding(string headers)
    {
        foreach (var line in headers.Split("\r\n", StringSplitOptions.None))
        {
            const string prefix = "Transfer-Encoding:";
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                line[prefix.Length..]
                    .Split(',')
                    .Any(value => string.Equals(value.Trim(), "chunked", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryCaptureRequest(
        byte[] requestBytes,
        int requestLength,
        int bodyStart,
        int? contentLength,
        bool isChunked,
        out CapturedRequest capturedRequest)
    {
        if (isChunked)
        {
            return TryCaptureChunkedRequest(
                requestBytes,
                requestLength,
                bodyStart,
                out capturedRequest);
        }

        var bodyLength = contentLength ?? 0;
        var messageLength = bodyStart + bodyLength;
        if (requestLength < messageLength)
        {
            capturedRequest = default!;
            return false;
        }

        capturedRequest = new CapturedRequest(
            Encoding.UTF8.GetString(requestBytes, 0, messageLength),
            Encoding.UTF8.GetString(requestBytes, bodyStart, bodyLength));
        return true;
    }

    private static bool TryCaptureChunkedRequest(
        byte[] requestBytes,
        int requestLength,
        int bodyStart,
        out CapturedRequest capturedRequest)
    {
        using var decodedBody = new MemoryStream();
        var cursor = bodyStart;

        while (true)
        {
            var chunkSizeLineEnd = IndexOfCrlf(requestBytes, cursor, requestLength);
            if (chunkSizeLineEnd < 0)
            {
                capturedRequest = default!;
                return false;
            }

            var chunkSizeLine = Encoding.ASCII.GetString(
                requestBytes,
                cursor,
                chunkSizeLineEnd - cursor);
            var extensionStart = chunkSizeLine.IndexOf(';');
            var chunkSizeText = (extensionStart >= 0
                    ? chunkSizeLine[..extensionStart]
                    : chunkSizeLine)
                .Trim();
            if (!int.TryParse(
                    chunkSizeText,
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out var chunkSize) ||
                chunkSize < 0 ||
                chunkSize > MaximumRequestBytes)
            {
                throw new InvalidOperationException("Loopback request contains an invalid chunk size.");
            }

            cursor = chunkSizeLineEnd + 2;
            if (chunkSize == 0)
            {
                var messageLength = FindChunkedMessageEnd(requestBytes, cursor, requestLength);
                if (messageLength < 0)
                {
                    capturedRequest = default!;
                    return false;
                }

                capturedRequest = new CapturedRequest(
                    Encoding.UTF8.GetString(requestBytes, 0, messageLength),
                    Encoding.UTF8.GetString(decodedBody.GetBuffer(), 0, checked((int)decodedBody.Length)));
                return true;
            }

            if (requestLength < cursor + chunkSize + 2)
            {
                capturedRequest = default!;
                return false;
            }

            decodedBody.Write(requestBytes, cursor, chunkSize);
            cursor += chunkSize;
            if (requestBytes[cursor] != '\r' || requestBytes[cursor + 1] != '\n')
            {
                throw new InvalidOperationException("Loopback request chunk is missing its terminator.");
            }

            cursor += 2;
        }
    }

    private static int FindChunkedMessageEnd(byte[] requestBytes, int trailerStart, int requestLength)
    {
        if (requestLength < trailerStart + 2)
        {
            return -1;
        }

        if (requestBytes[trailerStart] == '\r' && requestBytes[trailerStart + 1] == '\n')
        {
            return trailerStart + 2;
        }

        for (var index = trailerStart; index + 3 < requestLength; index++)
        {
            if (requestBytes[index] == '\r' &&
                requestBytes[index + 1] == '\n' &&
                requestBytes[index + 2] == '\r' &&
                requestBytes[index + 3] == '\n')
            {
                return index + 4;
            }
        }

        return -1;
    }

    private static int IndexOfCrlf(byte[] requestBytes, int start, int requestLength)
    {
        for (var index = start; index + 1 < requestLength; index++)
        {
            if (requestBytes[index] == '\r' && requestBytes[index + 1] == '\n')
            {
                return index;
            }
        }

        return -1;
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream,
        HttpStatusCode status,
        string body,
        CancellationToken cancellationToken)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var reason = status switch
        {
            HttpStatusCode.NoContent => "No Content",
            HttpStatusCode.OK => "OK",
            _ => status.ToString()
        };
        var responseHeaders = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {(int)status} {reason}\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n" +
            "Content-Type: application/json\r\n" +
            "Connection: close\r\n\r\n");

        await stream.WriteAsync(responseHeaders, cancellationToken);
        if (bodyBytes.Length > 0)
        {
            await stream.WriteAsync(bodyBytes, cancellationToken);
        }

        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<string> SelectRawRequestAsync(Task<CapturedRequest> request) =>
        (await request).RawRequest;

    private static async Task<string> SelectBodyAsync(Task<CapturedRequest> request) =>
        (await request).Body;

    private sealed record CapturedRequest(string RawRequest, string Body);
}

internal sealed class RecordingHttpLoggerProvider : ILoggerProvider
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _messages = new();

    public IReadOnlyCollection<string> Messages => _messages.ToArray();

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(_messages);

    public void Dispose()
    {
    }

    private sealed class RecordingLogger(
        System.Collections.Concurrent.ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            messages.Enqueue(formatter(state, exception));
            if (exception is not null)
            {
                messages.Enqueue(exception.ToString());
            }
        }
    }
}
