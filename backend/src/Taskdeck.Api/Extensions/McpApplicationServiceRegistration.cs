using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Extensions;

/// <summary>
/// Registers the minimal subset of Application services required by MCP
/// resources and tools. Both MCP stdio and MCP HTTP standalone modes call
/// this instead of the full <see cref="ApplicationServiceRegistration.AddApplicationServices"/>
/// which includes web-only services (SignalR notifiers, workers, LLM providers, etc.).
/// </summary>
public static class McpApplicationServiceRegistration
{
    /// <summary>
    /// Register the Application services that MCP resources and tools depend on.
    /// Deliberately skips web-only services (SignalR notifiers, workers,
    /// LLM providers, rate limiting, etc.) to keep the MCP host minimal.
    /// </summary>
    public static IServiceCollection AddMcpApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<AuthorizationService>();
        services.AddScoped<IAuthorizationService>(
            sp => sp.GetRequiredService<AuthorizationService>());
        services.AddScoped<BoardService>(sp =>
            new BoardService(
                sp.GetRequiredService<IUnitOfWork>(),
                sp.GetRequiredService<IAuthorizationService>()));
        services.AddScoped<ColumnService>();
        services.AddScoped<CardService>();
        services.AddScoped<LabelService>();
        services.AddScoped<AutomationProposalService>();
        services.AddScoped<IAutomationProposalService>(
            sp => sp.GetRequiredService<AutomationProposalService>());
        // ProposalRevisionService validates revised operation structure via the policy engine (#1281),
        // so the MCP container must be able to construct it (only dep is IUnitOfWork via AddInfrastructure).
        services.AddScoped<IAutomationPolicyEngine, AutomationPolicyEngine>();
        services.AddScoped<IProposalRevisionService, ProposalRevisionService>();
        // ADR-0065: the Context Fabric switches must reach every host that can create a capture —
        // the standalone MCP stdio/HTTP hosts included — or the dual-write flag would be honoured by
        // the web API and silently ignored by an MCP server writing the same database. TryAdd keeps
        // the web host's own registration (AddTaskdeckSettings) authoritative when both run.
        services.TryAddSingleton(sp =>
            sp.GetService<IConfiguration>()?.GetSection("ContextFabric").Get<ContextFabricSettings>()
            ?? new ContextFabricSettings());
        services.AddScoped<CaptureService>();
        services.AddScoped<ICaptureService>(
            sp => sp.GetRequiredService<CaptureService>());
        services.AddScoped<NotificationService>();
        services.AddScoped<INotificationService>(
            sp => sp.GetRequiredService<NotificationService>());

        return services;
    }
}
