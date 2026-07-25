using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;

namespace Taskdeck.Api.Extensions;

/// <summary>
/// Maps Taskdeck's authenticated Streamable HTTP endpoint consistently for both
/// the co-hosted API and the standalone HTTP MCP process.
/// </summary>
public static class McpEndpointMapping
{
    /// <summary>The only route on which Taskdeck serves MCP over HTTP.</summary>
    public const string HttpRoute = "/mcp";

    public static void MapTaskdeckMcpEndpoint(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var mcpEndpoint = endpoints.MapMcp(HttpRoute);
        // MCP bearer keys are not a browser credential. The co-hosted API's global
        // credentialed frontend policy must not enable cross-origin MCP requests.
        mcpEndpoint.WithMetadata(new DisableCorsAttribute());

        // The per-key request budget (McpPerApiKey) is NOT an endpoint-stage policy: it is enforced in
        // ApiKeyMiddleware via McpPerApiKeyRateLimiter (#1384), at the earliest point the validated key
        // ID is known — before the user-account lookup and last-used write — so a valid-but-over-quota
        // key cannot drive that per-request authentication-stage database work. Charging it here as
        // well would double-count requests, so this endpoint carries no rate-limiting metadata.
    }
}
