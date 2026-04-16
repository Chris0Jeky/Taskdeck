using Taskdeck.Api.Mcp;

namespace Taskdeck.Api.Extensions;

/// <summary>
/// Consolidates MCP telemetry service registration to avoid duplicate
/// AddSingleton calls across the three hosting modes (co-hosted, standalone HTTP, stdio).
/// </summary>
public static class McpTelemetryRegistration
{
    /// <summary>
    /// Registers MCP telemetry services (operation logger, etc.) into the DI container.
    /// Safe to call from any hosting mode.
    /// </summary>
    public static IServiceCollection AddMcpTelemetry(this IServiceCollection services)
    {
        services.AddSingleton<McpOperationLogger>();
        return services;
    }
}
