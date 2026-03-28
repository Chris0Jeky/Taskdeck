using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Services;

/// <summary>
/// Configuration for LLM kill switches. Bound from "LlmKillSwitch" configuration section.
/// Runtime modifications are held in memory (not persisted to config file).
/// </summary>
public class LlmKillSwitchSettings
{
    /// <summary>When true, all LLM calls are blocked.</summary>
    public bool GlobalKill { get; set; } = false;

    /// <summary>Surfaces that are individually killed (e.g. "Chat", "CaptureTriage", "Worker").</summary>
    public List<string> KilledSurfaces { get; set; } = new();

    /// <summary>User IDs that are individually killed.</summary>
    public List<string> KilledUserIds { get; set; } = new();
}
