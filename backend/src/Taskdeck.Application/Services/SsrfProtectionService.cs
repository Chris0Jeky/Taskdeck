namespace Taskdeck.Application.Services;

/// <summary>
/// Reusable SSRF protection service that validates URLs against private IP ranges,
/// cloud metadata endpoints, and other dangerous targets. Used for webhook endpoints,
/// LLM provider BaseUrl configuration, and any other user-provided URLs that the
/// server will make outbound requests to.
///
/// Cloud metadata hostnames and blocked IP ranges are maintained in a single source
/// of truth: <see cref="OutboundWebhookEndpointGuard"/>. This service delegates all
/// host-level checks there to avoid duplication.
/// </summary>
public static class SsrfProtectionService
{
    /// <summary>
    /// Validates a URL for SSRF safety. Returns a result indicating whether the URL
    /// is safe to make outbound requests to.
    /// </summary>
    /// <param name="url">The URL to validate.</param>
    /// <param name="allowLocalhostEndpoints">Whether to allow localhost URLs (for development).</param>
    /// <returns>A validation result with success/failure and an error message if blocked.</returns>
    public static SsrfValidationResult ValidateUrl(string? url, bool allowLocalhostEndpoints = false)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return SsrfValidationResult.Blocked("URL is required.");
        }

        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed))
        {
            return SsrfValidationResult.Blocked("URL must be an absolute URI.");
        }

        var isHttps = string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        var isHttp = string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
        if (!isHttps && !isHttp)
        {
            return SsrfValidationResult.Blocked("URL must use http or https scheme.");
        }

        if (!string.IsNullOrEmpty(parsed.UserInfo))
        {
            return SsrfValidationResult.Blocked("URL must not contain embedded credentials.");
        }

        var host = parsed.Host;

        // Delegate to the endpoint guard for comprehensive IP/hostname/metadata checking.
        // OutboundWebhookEndpointGuard is the single source of truth for blocked hosts.
        if (OutboundWebhookEndpointGuard.IsHostBlockedByStaticPolicy(host, allowLocalhostEndpoints))
        {
            return SsrfValidationResult.Blocked($"Host '{host}' is not allowed.");
        }

        return SsrfValidationResult.Allowed(parsed);
    }

    /// <summary>
    /// Validates a URL for SSRF safety with DNS resolution. This performs the static
    /// policy check first, then resolves the hostname via DNS and checks the resolved
    /// IP addresses against blocked ranges.
    /// </summary>
    /// <param name="url">The URL to validate.</param>
    /// <param name="allowLocalhostEndpoints">Whether to allow localhost URLs (for development).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A validation result with success/failure and an error message if blocked.</returns>
    public static async Task<SsrfValidationResult> ValidateUrlWithDnsAsync(
        string? url,
        bool allowLocalhostEndpoints = false,
        CancellationToken cancellationToken = default)
    {
        var staticResult = ValidateUrl(url, allowLocalhostEndpoints);
        if (!staticResult.IsAllowed)
        {
            return staticResult;
        }

        // Now resolve DNS and check the actual IP addresses
        var host = staticResult.ParsedUri!.Host;
        var isBlocked = await OutboundWebhookEndpointGuard.IsHostBlockedAsync(
            host,
            allowLocalhostEndpoints,
            cancellationToken);

        if (isBlocked)
        {
            return SsrfValidationResult.Blocked($"Host '{host}' resolves to a blocked IP address.");
        }

        return staticResult;
    }

    /// <summary>
    /// Validates an LLM provider BaseUrl for SSRF safety. This is a convenience method
    /// that checks the URL against SSRF protections and ensures it uses HTTPS
    /// (unless localhost is explicitly allowed for development).
    /// </summary>
    public static SsrfValidationResult ValidateLlmProviderUrl(
        string? baseUrl,
        bool allowLocalhostEndpoints = false)
    {
        var result = ValidateUrl(baseUrl, allowLocalhostEndpoints);
        if (!result.IsAllowed)
        {
            return result;
        }

        // LLM provider URLs must use HTTPS unless localhost is allowed
        var isHttps = string.Equals(result.ParsedUri!.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        var isLocalhost = string.Equals(result.ParsedUri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
        if (!isHttps && !(isLocalhost && allowLocalhostEndpoints))
        {
            return SsrfValidationResult.Blocked("LLM provider URL must use HTTPS.");
        }

        return result;
    }
}

/// <summary>
/// Result of an SSRF URL validation check.
/// </summary>
public sealed class SsrfValidationResult
{
    public bool IsAllowed { get; private init; }
    public string? ErrorMessage { get; private init; }
    public Uri? ParsedUri { get; private init; }

    private SsrfValidationResult() { }

    public static SsrfValidationResult Allowed(Uri parsedUri) =>
        new() { IsAllowed = true, ParsedUri = parsedUri };

    public static SsrfValidationResult Blocked(string errorMessage) =>
        new() { IsAllowed = false, ErrorMessage = errorMessage };
}
