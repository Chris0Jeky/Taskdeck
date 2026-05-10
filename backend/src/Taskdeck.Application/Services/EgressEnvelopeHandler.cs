using Microsoft.Extensions.Logging;
using Taskdeck.Domain.Agents;

namespace Taskdeck.Application.Services;

/// <summary>
/// DelegatingHandler that enforces the egress envelope for all outbound HTTP requests.
/// Rejects requests to hosts not in the EgressRegistry and blocks redirects to
/// out-of-envelope destinations.
/// GP-10: EgressViolations are loud, structured, and never swallowed.
/// </summary>
public sealed class EgressEnvelopeHandler : DelegatingHandler
{
    internal const long MaxRedirectReplayContentBytes = 1_048_576;

    private readonly IEgressRegistry _egressRegistry;
    private readonly ILogger<EgressEnvelopeHandler>? _logger;
    private readonly string? _sourceComponent;

    public EgressEnvelopeHandler(
        IEgressRegistry egressRegistry,
        ILogger<EgressEnvelopeHandler>? logger = null,
        string? sourceComponent = null)
    {
        _egressRegistry = egressRegistry ?? throw new ArgumentNullException(nameof(egressRegistry));
        _logger = logger;
        _sourceComponent = sourceComponent;
    }

    /// <summary>Maximum number of redirects to follow manually.</summary>
    private const int MaxRedirects = 10;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Validate the request host against the egress registry
        ValidateHost(request.RequestUri);

        // IMPORTANT: The HttpClient MUST be configured with AllowAutoRedirect = false
        // so that this handler sees 3xx responses and can validate redirect targets
        // against the egress allowlist. If auto-redirect is enabled, the handler only
        // sees the final response and the redirect check becomes ineffective.

        var replayContent = await PrepareReplayableContentAsync(request, cancellationToken);
        var currentRequest = request;
        var response = await base.SendAsync(currentRequest, cancellationToken);

        // Manually follow redirects, validating each target against the egress envelope
        var redirectCount = 0;
        while (IsRedirect(response) && response.Headers.Location is { } redirectUri && redirectCount < MaxRedirects)
        {
            redirectCount++;

            var resolvedRedirectUri = redirectUri.IsAbsoluteUri
                ? redirectUri
                : new Uri(currentRequest.RequestUri!, redirectUri);

            var redirectHost = resolvedRedirectUri.Host;

            if (string.IsNullOrWhiteSpace(redirectHost) || !_egressRegistry.IsHostAllowed(redirectHost))
            {
                var violation = new EgressViolation(
                    attemptedHost: redirectHost ?? "(empty)",
                    requestUri: resolvedRedirectUri.ToString(),
                    violationType: EgressViolationType.RedirectToUnknownHost,
                    reason: $"Redirect to host '{redirectHost}' is not in the egress envelope. Redirect blocked.",
                    sourceComponent: _sourceComponent);

                _logger?.LogError(
                    "EgressViolation: redirect to '{Host}' not in egress envelope. OriginalURI={OriginalUri}, RedirectURI={RedirectUri}, Source={Source}",
                    redirectHost, currentRequest.RequestUri, resolvedRedirectUri, _sourceComponent);

                throw new EgressViolationException(violation);
            }

            // Follow the redirect: create a new request preserving the method for 307/308
            var statusCode = (int)response.StatusCode;
            var previousRequest = currentRequest;
            var redirectRequest = new HttpRequestMessage
            {
                RequestUri = resolvedRedirectUri,
                Version = previousRequest.Version,
                Method = statusCode is 307 or 308 ? previousRequest.Method : HttpMethod.Get,
            };

            // 307/308 require preserving the original body and safe headers
            if (statusCode is 307 or 308)
            {
                if (previousRequest.Content is not null)
                {
                    if (replayContent is null)
                    {
                        response.Dispose();
                        throw new InvalidOperationException(
                            $"Cannot replay request content across a 307/308 redirect because the content length is unknown or exceeds {MaxRedirectReplayContentBytes} bytes.");
                    }

                    redirectRequest.Content = CreateReplayContent(replayContent);
                }

                var isCrossOrigin = !IsSameOrigin(previousRequest.RequestUri, resolvedRedirectUri);
                foreach (var header in previousRequest.Headers)
                {
                    if (string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (isCrossOrigin && IsSensitiveRedirectHeader(header.Key))
                        continue;
                    redirectRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
            else
            {
                replayContent = null;
            }

            response.Dispose();
            currentRequest = redirectRequest;
            response = await base.SendAsync(redirectRequest, cancellationToken);
        }

        return response;
    }

    private void ValidateHost(Uri? requestUri)
    {
        var host = requestUri?.Host;
        if (string.IsNullOrWhiteSpace(host))
        {
            var violation = new EgressViolation(
                attemptedHost: "(empty)",
                requestUri: requestUri?.ToString() ?? "(null)",
                violationType: EgressViolationType.UnknownHost,
                reason: "Request has no host specified.",
                sourceComponent: _sourceComponent);

            _logger?.LogError(
                "EgressViolation: request with no host. URI={Uri}, Source={Source}",
                requestUri, _sourceComponent);

            throw new EgressViolationException(violation);
        }

        if (!_egressRegistry.IsHostAllowed(host))
        {
            var violation = new EgressViolation(
                attemptedHost: host,
                requestUri: requestUri!.ToString(),
                violationType: EgressViolationType.UnknownHost,
                reason: $"Host '{host}' is not in the egress envelope. Request blocked.",
                sourceComponent: _sourceComponent);

            _logger?.LogError(
                "EgressViolation: host '{Host}' not in egress envelope. URI={Uri}, Source={Source}",
                host, requestUri, _sourceComponent);

            throw new EgressViolationException(violation);
        }
    }

    private static bool IsRedirect(HttpResponseMessage response)
    {
        var statusCode = (int)response.StatusCode;
        return statusCode is >= 300 and < 400;
    }

    private static async Task<ReplayableContent?> PrepareReplayableContentAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Content is null)
            return null;

        var contentLength = request.Content.Headers.ContentLength;
        if (contentLength is null || contentLength > MaxRedirectReplayContentBytes)
            return null;

        var headers = request.Content.Headers
            .Where(header => !string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
            .Select(header => new KeyValuePair<string, string[]>(header.Key, header.Value.ToArray()))
            .ToArray();

        var content = await request.Content.ReadAsByteArrayAsync(cancellationToken);
        if (content.LongLength > MaxRedirectReplayContentBytes)
        {
            throw new InvalidOperationException(
                $"Request content exceeds the {MaxRedirectReplayContentBytes} byte redirect replay limit.");
        }

        var originalContent = request.Content;
        var replayContent = new ReplayableContent(content, headers);
        request.Content = CreateReplayContent(replayContent);
        originalContent.Dispose();
        return replayContent;
    }

    private static ByteArrayContent CreateReplayContent(ReplayableContent replayContent)
    {
        var content = new ByteArrayContent(replayContent.Content);
        foreach (var header in replayContent.Headers)
        {
            content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return content;
    }

    private static bool IsSameOrigin(Uri? left, Uri right)
    {
        if (left is null)
            return false;

        return string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase)
               && string.Equals(left.Host, right.Host, StringComparison.OrdinalIgnoreCase)
               && left.Port == right.Port;
    }

    private static bool IsSensitiveRedirectHeader(string header)
        => string.Equals(header, "Authorization", StringComparison.OrdinalIgnoreCase)
           || string.Equals(header, "Proxy-Authorization", StringComparison.OrdinalIgnoreCase)
           || string.Equals(header, "Cookie", StringComparison.OrdinalIgnoreCase)
           || string.Equals(header, "x-goog-api-key", StringComparison.OrdinalIgnoreCase)
           || string.Equals(header, "x-api-key", StringComparison.OrdinalIgnoreCase)
           || string.Equals(header, "api-key", StringComparison.OrdinalIgnoreCase)
           || header.Contains("token", StringComparison.OrdinalIgnoreCase)
           || header.Contains("secret", StringComparison.OrdinalIgnoreCase);

    private sealed record ReplayableContent(byte[] Content, IReadOnlyList<KeyValuePair<string, string[]>> Headers);
}

/// <summary>
/// Exception thrown when an egress policy violation is detected.
/// Contains the structured <see cref="EgressViolation"/> for audit and logging.
/// This exception must never be caught and swallowed — it represents a security boundary.
/// </summary>
public sealed class EgressViolationException : Exception
{
    public EgressViolation Violation { get; }

    public EgressViolationException(EgressViolation violation)
        : base(violation.Reason)
    {
        Violation = violation ?? throw new ArgumentNullException(nameof(violation));
    }
}
