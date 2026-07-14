using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Taskdeck.Api.RateLimiting;

namespace Taskdeck.Api.Extensions;

/// <summary>
/// Maps Taskdeck's authenticated Streamable HTTP endpoint consistently for both
/// the co-hosted API and the standalone HTTP MCP process.
/// </summary>
public static class McpEndpointMapping
{
    /// <summary>The only route on which Taskdeck serves MCP over HTTP.</summary>
    public const string HttpRoute = "/mcp";

    public static void MapTaskdeckMcpEndpoint(
        this IEndpointRouteBuilder endpoints,
        bool rateLimitingEnabled)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var mcpEndpoint = endpoints.MapMcp(HttpRoute);
        // MCP bearer keys are not a browser credential. The co-hosted API's global
        // credentialed frontend policy must not enable cross-origin MCP requests.
        mcpEndpoint.WithMetadata(new DisableCorsAttribute());
        if (rateLimitingEnabled)
        {
            mcpEndpoint.RequireRateLimiting(RateLimitingPolicyNames.McpPerApiKey);
        }
    }
}
