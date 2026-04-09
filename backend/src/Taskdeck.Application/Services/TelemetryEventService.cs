using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Taskdeck.Application.Services;

/// <summary>
/// Records opt-in product telemetry events. Events are validated against the
/// taxonomy naming convention (noun.verb, lowercase, dot-separated) and logged
/// at Information level. A future iteration may persist events or forward them
/// to an analytics backend — this version provides the guard rails and validation.
/// </summary>
public sealed class TelemetryEventService : ITelemetryEventService
{
    private static readonly Regex EventNamePattern = new(
        @"^[a-z][a-z0-9_]*\.[a-z][a-z0-9_]*$",
        RegexOptions.Compiled);

    private readonly TelemetrySettings _settings;
    private readonly ILogger<TelemetryEventService> _logger;

    public TelemetryEventService(TelemetrySettings settings, ILogger<TelemetryEventService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public bool IsEnabled => _settings.Enabled;

    public bool RecordEvent(TelemetryEvent telemetryEvent)
    {
        if (!_settings.Enabled)
        {
            return false;
        }

        if (!ValidateEvent(telemetryEvent))
        {
            return false;
        }

        _logger.LogInformation(
            "Telemetry event recorded: {EventName} session={SessionId} mode={WorkspaceMode}",
            telemetryEvent.Event,
            telemetryEvent.SessionId,
            telemetryEvent.WorkspaceMode);

        return true;
    }

    public int RecordEvents(IReadOnlyList<TelemetryEvent> events)
    {
        if (!_settings.Enabled)
        {
            return 0;
        }

        if (events.Count > _settings.MaxBatchSize)
        {
            _logger.LogWarning(
                "Telemetry batch rejected: {Count} events exceeds max batch size {MaxBatchSize}",
                events.Count,
                _settings.MaxBatchSize);
            return 0;
        }

        var recorded = 0;
        foreach (var evt in events)
        {
            if (RecordEvent(evt))
            {
                recorded++;
            }
        }

        return recorded;
    }

    private bool ValidateEvent(TelemetryEvent telemetryEvent)
    {
        if (string.IsNullOrWhiteSpace(telemetryEvent.Event))
        {
            _logger.LogWarning("Telemetry event rejected: empty event name");
            return false;
        }

        if (!EventNamePattern.IsMatch(telemetryEvent.Event))
        {
            _logger.LogWarning(
                "Telemetry event rejected: invalid event name format '{EventName}' (expected noun.verb)",
                telemetryEvent.Event);
            return false;
        }

        if (string.IsNullOrWhiteSpace(telemetryEvent.SessionId))
        {
            _logger.LogWarning("Telemetry event rejected: empty session ID for event {EventName}", telemetryEvent.Event);
            return false;
        }

        return true;
    }
}
