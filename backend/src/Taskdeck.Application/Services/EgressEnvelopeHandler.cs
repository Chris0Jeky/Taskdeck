using System.Net;
using Taskdeck.Domain.Agents;

namespace Taskdeck.Application.Services;

/// <summary>
/// DelegatingHandler that enforces the egress envelope — any HTTP request to a
/// host not in the EgressRegistry is blocked with an EgressViolationException.
/// Also validates redirect targets to prevent open-redirect SSRF chains.
/// </summary>
public sealed class EgressEnvelopeHandler : DelegatingHandler
{
    private readonly IEgressRegistry _registry;
    private readonly string? _sourceComponent;

    /// <summary>
    /// Redirect status codes that indicate a location header should be validated.
    /// </summary>
    private static readonly HashSet<HttpStatusCode> RedirectCodes = new()
    {
        HttpStatusCode.Moved,
        HttpStatusCode.Redirect,
        HttpStatusCode.RedirectMethod,
        HttpStatusCode.TemporaryRedirect,
        HttpStatusCode.PermanentRedirect
    };

    public EgressEnvelopeHandler(IEgressRegistry registry, string? sourceComponent = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _sourceComponent = sourceComponent;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Validate the request URI host
        var requestUri = request.RequestUri;
        if (requestUri is null || string.IsNullOrWhiteSpace(requestUri.Host))
        {
            throw new EgressViolationException(new EgressViolation(
                attemptedHost: "(null)",
                requestUri: "(null)",
                violationType: EgressViolationType.UnknownHost,
                reason: "Request URI is null or has no host.",
                sourceComponent: _sourceComponent));
        }

        if (!_registry.IsHostAllowed(requestUri.Host))
        {
            throw new EgressViolationException(new EgressViolation(
                attemptedHost: requestUri.Host,
                requestUri: requestUri.ToString(),
                violationType: EgressViolationType.UnknownHost,
                reason: $"Host '{requestUri.Host}' is not in the egress envelope. Only registered hosts are allowed.",
                sourceComponent: _sourceComponent));
        }

        // Send the request
        var response = await base.SendAsync(request, cancellationToken);

        // Check redirect targets
        if (RedirectCodes.Contains(response.StatusCode) && response.Headers.Location is not null)
        {
            var redirectUri = response.Headers.Location;
            if (redirectUri.IsAbsoluteUri && !string.IsNullOrWhiteSpace(redirectUri.Host))
            {
                if (!_registry.IsHostAllowed(redirectUri.Host))
                {
                    throw new EgressViolationException(new EgressViolation(
                        attemptedHost: redirectUri.Host,
                        requestUri: redirectUri.ToString(),
                        violationType: EgressViolationType.RedirectToUnknownHost,
                        reason: $"Redirect to '{redirectUri.Host}' is not in the egress envelope.",
                        sourceComponent: _sourceComponent));
                }
            }
        }

        return response;
    }
}

/// <summary>
/// Exception thrown when an egress violation is detected (attempt to reach
/// a host outside the approved egress envelope).
/// </summary>
public sealed class EgressViolationException : Exception
{
    public EgressViolation Violation { get; }

    public EgressViolationException(EgressViolation violation)
        : base(violation.Reason)
    {
        Violation = violation;
    }
}
