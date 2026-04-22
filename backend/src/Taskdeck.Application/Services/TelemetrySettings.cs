using System.ComponentModel.DataAnnotations;

namespace Taskdeck.Application.Services;

/// <summary>
/// Configuration for opt-in product telemetry event recording.
/// Disabled by default — events are only recorded when explicitly enabled.
/// </summary>
public sealed class TelemetrySettings
{
    /// <summary>
    /// Master switch. Default: false (opt-in only).
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Maximum number of events accepted in a single batch request.
    /// </summary>
    [Range(1, 10000, ErrorMessage = "MaxBatchSize must be between 1 and 10000.")]
    public int MaxBatchSize { get; set; } = 100;
}
