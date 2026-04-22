using System.ComponentModel.DataAnnotations;

namespace Taskdeck.Application.Services;

public sealed class ObservabilitySettings
{
    public bool EnableOpenTelemetry { get; set; } = true;

    [Required(AllowEmptyStrings = false)]
    public string ServiceName { get; set; } = "Taskdeck.Api";

    public string? OtlpEndpoint { get; set; }
    public bool EnableConsoleExporter { get; set; }

    [Range(1, 3600, ErrorMessage = "MetricExportIntervalSeconds must be between 1 and 3600.")]
    public int MetricExportIntervalSeconds { get; set; } = 30;
}
