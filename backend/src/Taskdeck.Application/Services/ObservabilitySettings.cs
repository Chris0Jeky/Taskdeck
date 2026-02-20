namespace Taskdeck.Application.Services;

public sealed class ObservabilitySettings
{
    public bool EnableOpenTelemetry { get; set; } = true;
    public string ServiceName { get; set; } = "Taskdeck.Api";
    public string? OtlpEndpoint { get; set; }
    public bool EnableConsoleExporter { get; set; }
    public int MetricExportIntervalSeconds { get; set; } = 30;
}
