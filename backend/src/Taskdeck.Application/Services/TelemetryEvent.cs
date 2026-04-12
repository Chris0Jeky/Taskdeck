namespace Taskdeck.Application.Services;

/// <summary>
/// A product telemetry event following the noun.verb naming convention
/// defined in docs/product/TELEMETRY_TAXONOMY.md.
/// </summary>
public sealed class TelemetryEvent
{
    /// <summary>
    /// Event name in noun.verb format (e.g. "capture.submitted", "proposal.approved").
    /// </summary>
    public string Event { get; set; } = string.Empty;

    /// <summary>
    /// ISO 8601 UTC timestamp of the event.
    /// </summary>
    public string Timestamp { get; set; } = string.Empty;

    /// <summary>
    /// Anonymous session identifier, rotated on app restart.
    /// </summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// Current workspace mode: "guided", "workbench", or "agent".
    /// </summary>
    public string WorkspaceMode { get; set; } = string.Empty;

    /// <summary>
    /// Semver of the running application.
    /// </summary>
    public string AppVersion { get; set; } = string.Empty;

    /// <summary>
    /// Platform: "web", "desktop", or "cli".
    /// </summary>
    public string Platform { get; set; } = string.Empty;

    /// <summary>
    /// Additional event-specific properties. Keys and values must not contain PII.
    /// </summary>
    public Dictionary<string, object>? Properties { get; set; }
}
