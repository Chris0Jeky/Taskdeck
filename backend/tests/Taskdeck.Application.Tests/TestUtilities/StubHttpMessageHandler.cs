using System.Net.Http;
using Taskdeck.Application.Services;

namespace Taskdeck.Application.Tests.TestUtilities;

internal sealed class StubHttpMessageHandler : DelegatingHandler
{
    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : this((request, _) => Task.FromResult(responseFactory(request)))
    {
    }

    public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
    {
        InnerHandler = new ProtectedOutboundTelemetryHandler
        {
            InnerHandler = new CallbackHandler(responseFactory)
        };
    }

    private sealed class CallbackHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            responseFactory(request, cancellationToken);
    }
}
