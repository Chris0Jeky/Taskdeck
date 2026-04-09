namespace Taskdeck.Application.Services;

/// <summary>
/// Configuration for Sentry error tracking integration.
/// Disabled by default — must be explicitly enabled via configuration.
/// </summary>
public sealed class SentrySettings
{
    /// <summary>
    /// Master switch for Sentry integration. Default: false (opt-in only).
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Sentry DSN (Data Source Name). Required when Enabled is true.
    /// </summary>
    public string Dsn { get; set; } = string.Empty;

    /// <summary>
    /// Environment tag sent with events (e.g. "development", "production").
    /// </summary>
    public string Environment { get; set; } = "production";

    /// <summary>
    /// Sample rate for performance tracing (0.0 to 1.0). Default: 0.1 (10%).
    /// </summary>
    public double TracesSampleRate { get; set; } = 0.1;

    /// <summary>
    /// When true, PII scrubbing is enforced — Sentry SDK will not send
    /// usernames, emails, IP addresses, or request bodies.
    /// </summary>
    public bool SendDefaultPii { get; set; }
}
