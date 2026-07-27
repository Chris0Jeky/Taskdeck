using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Taskdeck.Api.Extensions;
using Taskdeck.Api.Workers;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Api.Tests;

public class ProtectedOutboundTelemetryHandlerTests
{
    private const string ControlClientName = "ProtectedOutboundTelemetryControl";

    [Fact]
    public async Task RegisteredProtectedClient_ShouldNotPropagateOrExportTraceContext()
    {
        var exporter = new RecordingActivityExporter();
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddHttpClientInstrumentation(options =>
            {
                options.FilterHttpRequestMessage = ObservabilityRegistration.ShouldExportHttpRequest;
            })
            .AddProcessor(new SimpleActivityExportProcessor(exporter))
            .Build();
        using var serviceProvider = BuildServiceProvider();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        var correlation = Guid.NewGuid().ToString("N");
        const string baggageKey = "taskdeck-correlation";
        var traceState = $"taskdeck={correlation}";
        var controlPath = $"/trace-control-{correlation}";
        var protectedPath = $"/trace-protected-{correlation}";
        await using var controlServer = new SingleRequestLoopbackServer(HttpStatusCode.NoContent);
        await using var protectedServer = new SingleRequestLoopbackServer(HttpStatusCode.NoContent);

        using var parentActivity = new Activity("protected-outbound-telemetry-test")
            .SetIdFormat(ActivityIdFormat.W3C)
            .AddBaggage(baggageKey, correlation)
            .Start();
        parentActivity.TraceStateString = traceState;
        parentActivity.ActivityTraceFlags |= ActivityTraceFlags.Recorded;
        using var controlClient = clientFactory.CreateClient(ControlClientName);
        using var protectedClient = clientFactory.CreateClient(nameof(OpenAiLlmProvider));

        using var controlResponse = await controlClient.GetAsync(controlServer.BuildUri(controlPath));
        using var protectedResponse = await protectedClient.GetAsync(protectedServer.BuildUri(protectedPath));
        var controlRequest = await controlServer.ReceivedRequest;
        var protectedRequest = await protectedServer.ReceivedRequest;

        controlResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        protectedResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        HasHeader(controlRequest, "traceparent").Should().BeTrue(
            "the registered control client must prove distributed tracing is active");
        HasHeader(controlRequest, "tracestate", traceState).Should().BeTrue(
            "the registered control client must propagate ambient trace state");
        HasBaggage(controlRequest, baggageKey, correlation).Should().BeTrue(
            "the registered control client must propagate ambient W3C baggage");
        HasHeader(protectedRequest, "traceparent").Should().BeFalse(
            "the protected primary handler disables trace-parent propagation on the wire");
        HasHeader(protectedRequest, "tracestate").Should().BeFalse(
            "the protected primary handler disables trace-state propagation on the wire");
        HasHeader(protectedRequest, "Correlation-Context").Should().BeFalse(
            "the protected primary handler disables default .NET baggage propagation on the wire");
        HasHeader(protectedRequest, "baggage").Should().BeFalse(
            "the protected primary handler disables baggage propagation on the wire");
        exporter.ExportedUrls.Should().Contain(url => url.Contains(controlPath, StringComparison.Ordinal));
        exporter.ExportedUrls.Should().NotContain(url => url.Contains(protectedPath, StringComparison.Ordinal),
            "Taskdeck's configured HTTP instrumentation filters marked protected requests");
    }

    [Fact]
    public async Task RegisteredProtectedClient_ShouldBeExcludedFromConfiguredHttpMetrics()
    {
        var exporter = new RecordingMetricExporter();
        using var serviceProvider = BuildServiceProvider(exporter);
        var meterProvider = serviceProvider.GetRequiredService<MeterProvider>();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        var correlation = Guid.NewGuid().ToString("N");
        await using var controlServer = new SingleRequestLoopbackServer(HttpStatusCode.NoContent);
        await using var protectedServer = new SingleRequestLoopbackServer(HttpStatusCode.NoContent);
        using var controlClient = clientFactory.CreateClient(ControlClientName);
        using var protectedClient = clientFactory.CreateClient(nameof(OpenAiLlmProvider));

        using var controlResponse = await controlClient.GetAsync(
            controlServer.BuildUri($"/metrics-control-{correlation}"));
        using var protectedResponse = await protectedClient.GetAsync(
            protectedServer.BuildUri($"/metrics-protected-{correlation}"));
        await controlServer.ReceivedRequest;
        await protectedServer.ReceivedRequest;

        controlResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        protectedResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        meterProvider.ForceFlush(5000).Should().BeTrue();

        exporter.Snapshots.Should().Contain(
            snapshot => snapshot.HasServerPort(controlServer.Port),
            "the normal registered client must prove Taskdeck's HTTP metric export is active");
        exporter.Snapshots.Should().NotContain(
            snapshot => snapshot.HasServerPort(protectedServer.Port),
            "protected server.address/server.port dimensions must not reach Taskdeck's configured OpenTelemetry exporter");
    }

    private static ServiceProvider BuildServiceProvider(BaseExporter<Metric>? metricExporter = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IWebHostEnvironment>(new TestWebHostEnvironment("Development"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:EnableLiveProviders"] = "true",
                ["Llm:AllowLiveProvidersInDevelopment"] = "true",
                ["Llm:Provider"] = "OpenAi",
                ["Llm:OpenAi:ApiKey"] = "test-openai-key",
                ["Llm:OpenAi:BaseUrl"] = "http://localhost:12345",
                ["Llm:OpenAi:Model"] = "test-openai-model",
                ["Llm:Ollama:AllowLocalhostEndpoints"] = "true"
            })
            .Build();

        services.AddLlmProviders(configuration);
        services.AddHttpClient(ControlClientName)
            .ConfigurePrimaryHttpMessageHandler(BuildControlPrimaryHandler);

        if (metricExporter is not null)
        {
            services.AddTaskdeckObservability(new ObservabilitySettings
            {
                EnableOpenTelemetry = true,
                EnableConsoleExporter = false,
                OtlpEndpoint = null,
                ServiceName = "Taskdeck.Api.Tests.ProtectedOutbound"
            });
            services.ConfigureOpenTelemetryMeterProvider(builder =>
                builder.AddReader(new BaseExportingMetricReader(metricExporter)));
        }

        return services.BuildServiceProvider();
    }

    private static SocketsHttpHandler BuildControlPrimaryHandler() => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        ConnectCallback = (context, cancellationToken) =>
            OutboundWebhookConnectCallback.ConnectAsync(
                context,
                allowLocalhostEndpoints: true,
                cancellationToken)
    };

    private static bool HasHeader(
        string rawRequest,
        string headerName,
        string? expectedValue = null)
    {
        var prefix = $"{headerName}:";
        return rawRequest
            .Split("\r\n", StringSplitOptions.None)
            .Any(line =>
                line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                (expectedValue is null ||
                 line[prefix.Length..].Trim().Contains(expectedValue, StringComparison.Ordinal)));
    }

    private static bool HasBaggage(string rawRequest, string key, string value)
    {
        const string prefix = "baggage:";
        return rawRequest
            .Split("\r\n", StringSplitOptions.None)
            .Where(line => line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .SelectMany(line => line[prefix.Length..].Split(','))
            .Any(member =>
            {
                var delimiter = member.IndexOf('=');
                return delimiter > 0 &&
                       string.Equals(member[..delimiter].Trim(), key, StringComparison.Ordinal) &&
                       string.Equals(member[(delimiter + 1)..].Trim(), value, StringComparison.Ordinal);
            });
    }

    private sealed class RecordingActivityExporter : BaseExporter<Activity>
    {
        private readonly ConcurrentQueue<string> _exportedUrls = new();

        internal IReadOnlyCollection<string> ExportedUrls => _exportedUrls.ToArray();

        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (var activity in batch)
            {
                var url = activity.GetTagItem("url.full")?.ToString() ??
                    activity.GetTagItem("http.url")?.ToString();
                if (!string.IsNullOrWhiteSpace(url))
                {
                    _exportedUrls.Enqueue(url);
                }
            }

            return ExportResult.Success;
        }
    }

    private sealed class RecordingMetricExporter : BaseExporter<Metric>
    {
        private readonly ConcurrentQueue<MetricSnapshot> _snapshots = new();

        internal IReadOnlyCollection<MetricSnapshot> Snapshots => _snapshots.ToArray();

        public override ExportResult Export(in Batch<Metric> batch)
        {
            foreach (var metric in batch)
            {
                foreach (ref readonly var point in metric.GetMetricPoints())
                {
                    var tags = new Dictionary<string, object?>(StringComparer.Ordinal);
                    foreach (var tag in point.Tags)
                    {
                        tags[tag.Key] = tag.Value;
                    }

                    _snapshots.Enqueue(new MetricSnapshot(
                        metric.Name,
                        tags));
                }
            }

            return ExportResult.Success;
        }
    }

    private sealed record MetricSnapshot(
        string Name,
        IReadOnlyDictionary<string, object?> Tags)
    {
        internal bool HasServerPort(int port) =>
            Tags.TryGetValue("server.port", out var value) &&
            string.Equals(
                Convert.ToString(value, CultureInfo.InvariantCulture),
                port.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
    }

    private sealed class TestWebHostEnvironment(string environmentName) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Taskdeck.Api.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = environmentName;
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
