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

    /// <summary>
    /// Allowlist of property keys that may appear in telemetry events.
    /// Only keys in this set are accepted — all others are stripped.
    /// This prevents callers from smuggling PII via arbitrary property keys.
    /// </summary>
    private static readonly HashSet<string> AllowedPropertyKeys = new(StringComparer.Ordinal)
    {
        "source", "has_attachment", "duration_ms", "count", "item_count",
        "workspace_mode", "error_code", "status", "method", "trigger",
        "result", "step", "provider", "platform", "format",
    };

    private const int MaxPropertyCount = 10;
    private const int MaxPropertyValueLength = 200;

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
            // Guard against null elements in the batch
            if (evt == null)
            {
                _logger.LogWarning("Telemetry event rejected: null element in batch");
                continue;
            }

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

        // Sanitize properties: strip disallowed keys, cap size, truncate values.
        // This prevents PII from being smuggled via arbitrary property fields.
        if (telemetryEvent.Properties != null)
        {
            if (telemetryEvent.Properties.Count > MaxPropertyCount)
            {
                _logger.LogWarning(
                    "Telemetry event {EventName}: properties truncated from {Count} to {Max}",
                    telemetryEvent.Event, telemetryEvent.Properties.Count, MaxPropertyCount);
            }

            var sanitized = new Dictionary<string, object>();
            foreach (var kvp in telemetryEvent.Properties)
            {
                if (sanitized.Count >= MaxPropertyCount) break;

                if (!AllowedPropertyKeys.Contains(kvp.Key))
                {
                    _logger.LogDebug(
                        "Telemetry event {EventName}: stripped disallowed property key '{Key}'",
                        telemetryEvent.Event, kvp.Key);
                    continue;
                }

                // Truncate string values to prevent large payloads
                var value = kvp.Value;
                if (value is string strValue && strValue.Length > MaxPropertyValueLength)
                {
                    value = strValue[..MaxPropertyValueLength];
                }

                sanitized[kvp.Key] = value;
            }

            telemetryEvent.Properties = sanitized;
        }

        return true;
    }
}
