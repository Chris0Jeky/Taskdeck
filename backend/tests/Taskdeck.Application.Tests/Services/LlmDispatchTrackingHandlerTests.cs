using FluentAssertions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class LlmDispatchTrackingHandlerTests
{
    [Fact]
    public async Task SendAsync_DoesNotMarkAlreadyCancelledRequest()
    {
        var context = new LlmDispatchContext();
        context.Observe("provider", "model");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        LlmDispatchTrackingHandler.Attach(request, context);
        using var handler = new LlmDispatchTrackingHandler
        {
            InnerHandler = new SnapshotHandler(context)
        };
        using var invoker = new HttpMessageInvoker(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => invoker.SendAsync(request, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        context.ReadSnapshot().Phase.Should().Be(LlmDispatchPhase.ObservedPreDispatch);
    }

    [Fact]
    public async Task SendAsync_MarksDispatchedBeforeAwaitingTransport()
    {
        var context = new LlmDispatchContext();
        context.Observe("provider", "model");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        LlmDispatchTrackingHandler.Attach(request, context);
        var transport = new SnapshotHandler(context);
        using var handler = new LlmDispatchTrackingHandler { InnerHandler = transport };
        using var invoker = new HttpMessageInvoker(handler);

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        transport.PhaseAtDispatch.Should().Be(LlmDispatchPhase.Dispatched);
        context.ReadSnapshot().Should().Be(new LlmDispatchSnapshot(
            LlmDispatchPhase.Dispatched,
            "provider",
            "model"));
    }

    private sealed class SnapshotHandler(LlmDispatchContext context) : HttpMessageHandler
    {
        public LlmDispatchPhase PhaseAtDispatch { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            PhaseAtDispatch = context.ReadSnapshot().Phase;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NoContent));
        }
    }
}
