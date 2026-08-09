namespace Taskdeck.Application.Services;

/// <summary>
/// Keeps protected request destinations out of ambient HTTP diagnostics until the
/// request reaches the application-owned transport boundary.
/// </summary>
public sealed class ProtectedOutboundTelemetryHandler : DelegatingHandler
{
    private static readonly Uri MaskedRequestUri = new("https://protected-outbound.invalid/");
    private static readonly HttpRequestOptionsKey<Uri> OriginalRequestUriKey =
        new("Taskdeck.ProtectedOutboundOriginalRequestUri");
    private static readonly HttpRequestOptionsKey<bool> SuppressTelemetryKey =
        new("Taskdeck.SuppressProtectedOutboundTelemetry");

    /// <summary>
    /// Stores an absolute destination privately and exposes only a constant placeholder
    /// to telemetry emitted by <see cref="HttpClient"/> before its handler chain runs.
    /// </summary>
    public static void PrepareForSend(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.Options.TryGetValue(OriginalRequestUriKey, out _))
        {
            var requestUri = request.RequestUri ?? throw new InvalidOperationException(
                "Protected outbound requests require an absolute request URI.");
            if (!requestUri.IsAbsoluteUri)
            {
                throw new InvalidOperationException(
                    "Protected outbound requests require an absolute request URI.");
            }

            request.Options.Set(OriginalRequestUriKey, requestUri);
        }

        request.Options.Set(SuppressTelemetryKey, true);
        request.RequestUri = MaskedRequestUri;
    }

    public static bool ShouldSuppressTelemetry(HttpRequestMessage request) =>
        request.Options.TryGetValue(SuppressTelemetryKey, out var suppress) && suppress;

    internal static bool IsRequestUriMasked(HttpRequestMessage request) =>
        request.Options.TryGetValue(OriginalRequestUriKey, out _) &&
        request.RequestUri == MaskedRequestUri;

    protected override HttpResponseMessage Send(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        MarkProtected(request);
        try
        {
            RestoreOriginalRequestUri(request);
            return base.Send(request, cancellationToken);
        }
        finally
        {
            RemaskRequestUri(request);
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        MarkProtected(request);
        try
        {
            RestoreOriginalRequestUri(request);
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            RemaskRequestUri(request);
        }
    }

    private static void RestoreOriginalRequestUri(HttpRequestMessage request)
    {
        if (!request.Options.TryGetValue(OriginalRequestUriKey, out var originalRequestUri))
        {
            throw new InvalidOperationException(
                "Protected outbound requests must be prepared before HttpClient.SendAsync.");
        }

        request.RequestUri = originalRequestUri;
    }

    private static void RemaskRequestUri(HttpRequestMessage request) =>
        request.RequestUri = MaskedRequestUri;

    private static void MarkProtected(HttpRequestMessage request) =>
        request.Options.Set(SuppressTelemetryKey, true);
}
