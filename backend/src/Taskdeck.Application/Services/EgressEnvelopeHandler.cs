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

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Validate the request host against the egress registry
        var host = request.RequestUri?.Host;
        if (string.IsNullOrWhiteSpace(host))
        {
            var violation = new EgressViolation(
                attemptedHost: "(empty)",
                requestUri: request.RequestUri?.ToString() ?? "(null)",
                violationType: EgressViolationType.UnknownHost,
                reason: "Request has no host specified.",
                sourceComponent: _sourceComponent);

            _logger?.LogError(
                "EgressViolation: request with no host. URI={Uri}, Source={Source}",
                request.RequestUri, _sourceComponent);

            throw new EgressViolationException(violation);
        }

        if (!_egressRegistry.IsHostAllowed(host))
        {
            var violation = new EgressViolation(
                attemptedHost: host,
                requestUri: request.RequestUri!.ToString(),
                violationType: EgressViolationType.UnknownHost,
                reason: $"Host '{host}' is not in the egress envelope. Request blocked.",
                sourceComponent: _sourceComponent);

            _logger?.LogError(
                "EgressViolation: host '{Host}' not in egress envelope. URI={Uri}, Source={Source}",
                host, request.RequestUri, _sourceComponent);

            throw new EgressViolationException(violation);
        }

        var response = await base.SendAsync(request, cancellationToken);

        // Check redirect targets — block redirects to out-of-envelope hosts
        if (IsRedirect(response) && response.Headers.Location is { } redirectUri)
        {
            var redirectHost = redirectUri.IsAbsoluteUri
                ? redirectUri.Host
                : request.RequestUri?.Host;

            if (!string.IsNullOrWhiteSpace(redirectHost) && !_egressRegistry.IsHostAllowed(redirectHost))
            {
                var violation = new EgressViolation(
                    attemptedHost: redirectHost,
                    requestUri: redirectUri.ToString(),
                    violationType: EgressViolationType.RedirectToUnknownHost,
                    reason: $"Redirect to host '{redirectHost}' is not in the egress envelope. Redirect blocked.",
                    sourceComponent: _sourceComponent);

                _logger?.LogError(
                    "EgressViolation: redirect to '{Host}' not in egress envelope. OriginalURI={OriginalUri}, RedirectURI={RedirectUri}, Source={Source}",
                    redirectHost, request.RequestUri, redirectUri, _sourceComponent);

                throw new EgressViolationException(violation);
            }
        }

        return response;
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
