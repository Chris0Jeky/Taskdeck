namespace Taskdeck.Application.Services;

/// <summary>
/// Configuration for self-hosted web analytics (Plausible or Umami).
/// Disabled by default — the frontend reads these settings from a config endpoint
/// and injects the analytics script only when configured.
/// </summary>
public sealed class AnalyticsSettings
{
    /// <summary>
    /// Master switch. Default: false (opt-in only).
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Analytics provider: "plausible" or "umami". Case-insensitive.
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Full URL to the analytics script (e.g. "https://plausible.example.com/js/script.js").
    /// </summary>
    public string ScriptUrl { get; set; } = string.Empty;

    /// <summary>
    /// Site identifier / website ID used by the analytics provider.
    /// </summary>
    public string SiteId { get; set; } = string.Empty;
}
