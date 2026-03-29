using Taskdeck.Domain.Agents;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Services;

/// <summary>
/// Concrete, immutable implementation of <see cref="ITaskdeckTool"/> used
/// to register built-in tools in the tool registry.
/// </summary>
public sealed record TaskdeckToolDefinition(
    string Key,
    string DisplayName,
    string Description,
    ToolScope Scope,
    ToolRiskLevel RiskLevel) : ITaskdeckTool;
