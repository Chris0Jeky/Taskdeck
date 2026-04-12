namespace Taskdeck.Application.Services;

/// <summary>
/// Records opt-in product telemetry events aligned with the taxonomy
/// defined in docs/product/TELEMETRY_TAXONOMY.md.
/// </summary>
public interface ITelemetryEventService
{
    /// <summary>
    /// Records a single telemetry event. Returns false if telemetry is disabled
    /// or the event fails validation.
    /// </summary>
    bool RecordEvent(TelemetryEvent telemetryEvent);

    /// <summary>
    /// Records a batch of telemetry events. Returns the count of successfully recorded events.
    /// </summary>
    int RecordEvents(IReadOnlyList<TelemetryEvent> events);

    /// <summary>
    /// Whether telemetry recording is currently enabled.
    /// </summary>
    bool IsEnabled { get; }
}
