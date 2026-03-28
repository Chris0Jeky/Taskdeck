using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Taskdeck.Api.Telemetry;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Extensions;

public static class ObservabilityRegistration
{
    public static IServiceCollection AddTaskdeckObservability(
        this IServiceCollection services,
        ObservabilitySettings observabilitySettings)
    {
        if (!observabilitySettings.EnableOpenTelemetry)
        {
            return services;
        }

        var openTelemetryBuilder = services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(observabilitySettings.ServiceName));

        openTelemetryBuilder.WithTracing(tracing =>
        {
            tracing
                .AddAspNetCoreInstrumentation(options =>
                {
                    // Raw exception events can capture sensitive request/body data before our
                    // sanitized logging path runs, so keep automatic exception recording off.
                    options.RecordException = false;
                })
                .AddHttpClientInstrumentation()
                .AddSource(TaskdeckTelemetry.ActivitySourceName);

            if (!string.IsNullOrWhiteSpace(observabilitySettings.OtlpEndpoint) &&
                Uri.TryCreate(observabilitySettings.OtlpEndpoint, UriKind.Absolute, out var traceEndpoint))
            {
                tracing.AddOtlpExporter(options =>
                {
                    options.Endpoint = traceEndpoint;
                    options.Protocol = OtlpExportProtocol.Grpc;
                });
            }

            if (observabilitySettings.EnableConsoleExporter)
            {
                tracing.AddConsoleExporter();
            }
        });

        openTelemetryBuilder.WithMetrics(metrics =>
        {
            metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter(TaskdeckTelemetry.MeterName);

            if (!string.IsNullOrWhiteSpace(observabilitySettings.OtlpEndpoint) &&
                Uri.TryCreate(observabilitySettings.OtlpEndpoint, UriKind.Absolute, out var metricEndpoint))
            {
                metrics.AddOtlpExporter(options =>
                {
                    options.Endpoint = metricEndpoint;
                    options.Protocol = OtlpExportProtocol.Grpc;
                });
            }

            if (observabilitySettings.EnableConsoleExporter)
            {
                metrics.AddConsoleExporter(
                    (_, readerOptions) =>
                    {
                        readerOptions.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds =
                            Math.Max(observabilitySettings.MetricExportIntervalSeconds, 5) * 1000;
                    });
            }
        });

        return services;
    }
}
