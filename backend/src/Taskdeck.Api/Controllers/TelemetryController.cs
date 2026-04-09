using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

/// <summary>
/// Endpoints for opt-in product telemetry event recording and client configuration.
/// All telemetry is disabled by default and requires explicit opt-in.
/// </summary>
[ApiController]
[Route("api/telemetry")]
public class TelemetryController : ControllerBase
{
    private readonly ITelemetryEventService _telemetryEventService;
    private readonly SentrySettings _sentrySettings;
    private readonly AnalyticsSettings _analyticsSettings;
    private readonly TelemetrySettings _telemetrySettings;

    public TelemetryController(
        ITelemetryEventService telemetryEventService,
        SentrySettings sentrySettings,
        AnalyticsSettings analyticsSettings,
        TelemetrySettings telemetrySettings)
    {
        _telemetryEventService = telemetryEventService;
        _sentrySettings = sentrySettings;
        _analyticsSettings = analyticsSettings;
        _telemetrySettings = telemetrySettings;
    }

    /// <summary>
    /// Returns client-side telemetry configuration. The frontend uses this to
    /// determine which integrations are available and how to initialize them.
    /// DSNs and script URLs are only returned when the corresponding integration
    /// is enabled. No secrets or API keys are exposed.
    /// </summary>
    [HttpGet("config")]
    [AllowAnonymous]
    public IActionResult GetConfig()
    {
        return Ok(new ClientTelemetryConfigResponse
        {
            Sentry = new SentryClientConfig
            {
                Enabled = _sentrySettings.Enabled,
                Dsn = _sentrySettings.Enabled ? _sentrySettings.Dsn : string.Empty,
                Environment = _sentrySettings.Environment,
                TracesSampleRate = _sentrySettings.TracesSampleRate,
            },
            Analytics = new AnalyticsClientConfig
            {
                Enabled = _analyticsSettings.Enabled,
                Provider = _analyticsSettings.Enabled ? _analyticsSettings.Provider : string.Empty,
                ScriptUrl = _analyticsSettings.Enabled ? _analyticsSettings.ScriptUrl : string.Empty,
                SiteId = _analyticsSettings.Enabled ? _analyticsSettings.SiteId : string.Empty,
            },
            Telemetry = new TelemetryClientConfig
            {
                Enabled = _telemetrySettings.Enabled,
            },
        });
    }

    /// <summary>
    /// Records a batch of product telemetry events. Requires authentication.
    /// Events are validated against the taxonomy naming convention and rejected
    /// if telemetry is disabled on the server.
    /// </summary>
    [HttpPost("events")]
    [Authorize]
    public IActionResult RecordEvents([FromBody] TelemetryBatchRequest request)
    {
        if (!_telemetryEventService.IsEnabled)
        {
            return Ok(new { recorded = 0, message = "Telemetry is disabled on this server." });
        }

        if (request.Events == null || request.Events.Count == 0)
        {
            return BadRequest(new { error = "No events provided." });
        }

        var recorded = _telemetryEventService.RecordEvents(request.Events);
        return Ok(new { recorded });
    }
}

public sealed class ClientTelemetryConfigResponse
{
    public SentryClientConfig Sentry { get; set; } = new();
    public AnalyticsClientConfig Analytics { get; set; } = new();
    public TelemetryClientConfig Telemetry { get; set; } = new();
}

public sealed class SentryClientConfig
{
    public bool Enabled { get; set; }
    public string Dsn { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public double TracesSampleRate { get; set; }
}

public sealed class AnalyticsClientConfig
{
    public bool Enabled { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ScriptUrl { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
}

public sealed class TelemetryClientConfig
{
    public bool Enabled { get; set; }
}

public sealed class TelemetryBatchRequest
{
    public List<TelemetryEvent> Events { get; set; } = new();
}
