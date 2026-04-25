using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using Taskdeck.Api.Mcp;

namespace Taskdeck.Api.Extensions;

/// <summary>
/// Registers the shared set of MCP resources and tools used by all three
/// hosting modes (co-hosted web, standalone HTTP, and stdio).
/// </summary>
public static class McpResourcesAndToolsRegistration
{
    /// <summary>
    /// Add MCP resources (Board, Capture, Proposal) and tools (Read, Write, Proposal)
    /// to the given MCP server builder.
    /// </summary>
    public static IMcpServerBuilder AddMcpResourcesAndTools(this IMcpServerBuilder builder)
    {
        return builder
            .WithResources<BoardResources>()
            .WithResources<CaptureResources>()
            .WithResources<ProposalResources>()
            .WithTools<ReadTools>()
            .WithTools<WriteTools>()
            .WithTools<ProposalTools>();
    }
}
