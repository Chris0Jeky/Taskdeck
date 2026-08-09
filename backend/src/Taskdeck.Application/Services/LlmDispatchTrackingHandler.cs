namespace Taskdeck.Application.Services;

/// <summary>
/// Marks a compatible-provider request as dispatched only after every outer
/// validation, egress, telemetry, and circuit handler has admitted it.
/// </summary>
internal sealed class LlmDispatchTrackingHandler : DelegatingHandler
{
    private static readonly HttpRequestOptionsKey<LlmDispatchContext> DispatchContextKey =
        new("Taskdeck.LlmDispatchContext");

    internal static void Attach(HttpRequestMessage message, LlmDispatchContext dispatchContext)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(dispatchContext);
        message.Options.Set(DispatchContextKey, dispatchContext);
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Options.TryGetValue(DispatchContextKey, out var dispatchContext))
            dispatchContext.MarkDispatched();

        return base.SendAsync(request, cancellationToken);
    }
}
