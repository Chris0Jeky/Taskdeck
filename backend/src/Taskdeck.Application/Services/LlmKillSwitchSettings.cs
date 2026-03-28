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

    /// <summary>Reason provided when the global kill switch was activated.</summary>
    public string? GlobalKillReason { get; set; }

    /// <summary>Surfaces that are individually killed (e.g. "Chat", "CaptureTriage", "Worker").</summary>
    public HashSet<string> KilledSurfaces { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>User IDs that are individually killed.</summary>
    public HashSet<string> KilledUserIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Reasons keyed by surface name or user ID.</summary>
    public Dictionary<string, string> Reasons { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
