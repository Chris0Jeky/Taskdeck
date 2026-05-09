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

        var response = await base.SendAsync(request, cancellationToken);

        // Manually follow redirects, validating each target against the egress envelope
        var redirectCount = 0;
        while (IsRedirect(response) && response.Headers.Location is { } redirectUri && redirectCount < MaxRedirects)
        {
            redirectCount++;

            var resolvedRedirectUri = redirectUri.IsAbsoluteUri
                ? redirectUri
                : new Uri(request.RequestUri!, redirectUri);

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
                    redirectHost, request.RequestUri, resolvedRedirectUri, _sourceComponent);

                throw new EgressViolationException(violation);
            }

            // Follow the redirect: create a new request preserving the method for 307/308
            var statusCode = (int)response.StatusCode;
            var redirectRequest = new HttpRequestMessage
            {
                RequestUri = resolvedRedirectUri,
                Version = request.Version,
                Method = statusCode is 307 or 308 ? request.Method : HttpMethod.Get,
            };

            // 307/308 require preserving the original headers and body
            if (statusCode is 307 or 308)
            {
                redirectRequest.Content = request.Content;
                foreach (var header in request.Headers)
                {
                    redirectRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            response.Dispose();
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
