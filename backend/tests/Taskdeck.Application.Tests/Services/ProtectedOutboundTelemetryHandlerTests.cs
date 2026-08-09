using System.Collections.Concurrent;
using System.Net;
using FluentAssertions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class ProtectedOutboundTelemetryHandlerTests
{
    [Fact]
    public async Task SuccessfulSend_ShouldRestoreOriginalUriOnlyForTransportAndRemaskAfterward()
    {
        var originalUri = new Uri("https://provider.example/v1/complete?marker=success-secret");
        Uri? transportedUri = null;
        using var invoker = BuildInvoker(new CallbackHandler((request, _) =>
        {
            transportedUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }));
        using var request = new HttpRequestMessage(HttpMethod.Post, originalUri);

        ProtectedOutboundTelemetryHandler.PrepareForSend(request);
        ProtectedOutboundTelemetryHandler.IsRequestUriMasked(request).Should().BeTrue();

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        transportedUri.Should().Be(originalUri);
        ProtectedOutboundTelemetryHandler.IsRequestUriMasked(request).Should().BeTrue(
            "the true URI must not remain exposed after a successful transport call");
    }

    [Fact]
    public async Task ExceptionalTransport_ShouldRemaskAfterFailure()
    {
        var originalUri = new Uri("https://provider.example/v1/complete?marker=exception-secret");
        Uri? transportedUri = null;
        using var invoker = BuildInvoker(new CallbackHandler((request, _) =>
        {
            transportedUri = request.RequestUri;
            throw new InvalidOperationException("Transport failed.");
        }));
        using var request = new HttpRequestMessage(HttpMethod.Post, originalUri);
        ProtectedOutboundTelemetryHandler.PrepareForSend(request);

        var act = () => invoker.SendAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        transportedUri.Should().Be(originalUri);
        ProtectedOutboundTelemetryHandler.IsRequestUriMasked(request).Should().BeTrue(
            "exceptional transports must not leave the true URI exposed");
    }

    [Fact]
    public async Task CancelledTransport_ShouldRemaskAfterCancellation()
    {
        var originalUri = new Uri("https://provider.example/v1/complete?marker=cancel-secret");
        var enteredTransport = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var invoker = BuildInvoker(new CallbackHandler(async (request, cancellationToken) =>
        {
            enteredTransport.TrySetResult(request.RequestUri!);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }));
        using var request = new HttpRequestMessage(HttpMethod.Post, originalUri);
        using var cancellationSource = new CancellationTokenSource();
        ProtectedOutboundTelemetryHandler.PrepareForSend(request);

        var sendTask = invoker.SendAsync(request, cancellationSource.Token);
        (await enteredTransport.Task.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be(originalUri);
        cancellationSource.Cancel();
        var cancellationAct = async () => await sendTask;

        await cancellationAct.Should().ThrowAsync<OperationCanceledException>();
        ProtectedOutboundTelemetryHandler.IsRequestUriMasked(request).Should().BeTrue(
            "cancellation must run the handler's remasking finally block");
    }

    [Fact]
    public async Task RetryHandler_ShouldReceiveMaskedRequestBetweenAttempts()
    {
        var originalUri = new Uri("https://provider.example/v1/complete?marker=retry-secret");
        var terminal = new FailOnceHandler();
        var protectedHandler = new ProtectedOutboundTelemetryHandler
        {
            InnerHandler = terminal
        };
        var retryHandler = new RetryOnceHandler
        {
            InnerHandler = protectedHandler
        };
        using var invoker = new HttpMessageInvoker(retryHandler);
        using var request = new HttpRequestMessage(HttpMethod.Post, originalUri);
        ProtectedOutboundTelemetryHandler.PrepareForSend(request);

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        terminal.TransportUris.Should().Equal(originalUri, originalUri);
        retryHandler.WasMaskedBeforeRetry.Should().BeTrue(
            "the first failed attempt must remask before an outer policy retries");
        ProtectedOutboundTelemetryHandler.IsRequestUriMasked(request).Should().BeTrue(
            "the final successful retry must also remask the request");
    }

    private static HttpMessageInvoker BuildInvoker(HttpMessageHandler terminalHandler) =>
        new(new ProtectedOutboundTelemetryHandler
        {
            InnerHandler = terminalHandler
        });

    private sealed class CallbackHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            callback(request, cancellationToken);
    }

    private sealed class FailOnceHandler : HttpMessageHandler
    {
        private readonly ConcurrentQueue<Uri> _transportUris = new();
        private int _attempts;

        internal IReadOnlyCollection<Uri> TransportUris => _transportUris.ToArray();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _transportUris.Enqueue(request.RequestUri!);
            if (Interlocked.Increment(ref _attempts) == 1)
            {
                throw new HttpRequestException("Retryable transport failure.");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }

    private sealed class RetryOnceHandler : DelegatingHandler
    {
        internal bool WasMaskedBeforeRetry { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            try
            {
                return await base.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException)
            {
                WasMaskedBeforeRetry = ProtectedOutboundTelemetryHandler.IsRequestUriMasked(request);
                return await base.SendAsync(request, cancellationToken);
            }
        }
    }
}
