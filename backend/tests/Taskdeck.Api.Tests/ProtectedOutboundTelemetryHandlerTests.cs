using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Sentry;
using Sentry.AspNetCore;
using Sentry.Extensibility;
using Sentry.Protocol.Envelopes;
using Taskdeck.Api.Extensions;
using Taskdeck.Api.Workers;
using Taskdeck.Application.Services;
using Taskdeck.Infrastructure.Connectors;
using Xunit;

namespace Taskdeck.Api.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SentryGlobalStateCollection
{
    public const string Name = "Sentry global state";
}

[Collection(SentryGlobalStateCollection.Name)]
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
        using var protectedMessage = new HttpRequestMessage(HttpMethod.Get, protectedServer.BuildUri(protectedPath));
        ProtectedOutboundTelemetryHandler.PrepareForSend(protectedMessage);
        using var protectedResponse = await protectedClient.SendAsync(protectedMessage);
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
        using var protectedMessage = new HttpRequestMessage(
            HttpMethod.Get,
            protectedServer.BuildUri($"/metrics-protected-{correlation}"));
        ProtectedOutboundTelemetryHandler.PrepareForSend(protectedMessage);
        using var protectedResponse = await protectedClient.SendAsync(protectedMessage);
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

    [Fact]
    public async Task RegisteredProtectedClient_ShouldMaskSystemNetHttpEventsAndPreserveWireUri()
    {
        using var eventListener = new RecordingHttpEventListener();
        using var serviceProvider = BuildServiceProvider();
        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        var controlMarker = $"event-control-{Guid.NewGuid():N}";
        var protectedMarker = $"event-protected-{Guid.NewGuid():N}";
        var controlPathAndQuery = $"/control?marker={controlMarker}";
        var protectedPathAndQuery = $"/protected?marker={protectedMarker}";
        await using var controlServer = new SingleRequestLoopbackServer(HttpStatusCode.NoContent);
        await using var protectedServer = new SingleRequestLoopbackServer(HttpStatusCode.NoContent);
        using var controlClient = clientFactory.CreateClient(ControlClientName);
        using var protectedClient = clientFactory.CreateClient(nameof(OpenAiLlmProvider));

        using var controlResponse = await controlClient.GetAsync(controlServer.BuildUri(controlPathAndQuery));
        using var protectedRequest = new HttpRequestMessage(
            HttpMethod.Get,
            protectedServer.BuildUri(protectedPathAndQuery));
        ProtectedOutboundTelemetryHandler.PrepareForSend(protectedRequest);
        using var protectedResponse = await protectedClient.SendAsync(protectedRequest);
        var controlWireRequest = await controlServer.ReceivedRequest;
        var protectedWireRequest = await protectedServer.ReceivedRequest;

        controlResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        protectedResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        controlWireRequest.Should().Contain($"GET {controlPathAndQuery} HTTP/1.1");
        protectedWireRequest.Should().Contain($"GET {protectedPathAndQuery} HTTP/1.1",
            "the transport handler must restore the true destination before connecting");
        eventListener.Payloads.Should().Contain(
            payload => payload.Contains(controlMarker, StringComparison.Ordinal),
            "the plain factory client must prove System.Net.Http EventSource capture is active");
        eventListener.Payloads.Should().NotContain(
            payload => payload.Contains(protectedMarker, StringComparison.Ordinal),
            "the protected path and query must be masked before HttpClient emits RequestStart");
        protectedRequest.RequestUri!.AbsoluteUri.Should().NotContain(protectedMarker,
            "the request must be remasked after the permitted transport completes");
    }

    [Fact]
    public async Task RegisteredProvider_ShouldPrepareBeforeSystemNetHttpEventsAndReachConfiguredOrigin()
    {
        using var eventListener = new RecordingHttpEventListener();
        var controlMarker = $"provider-control-{Guid.NewGuid():N}";
        var protectedMarker = $"provider-protected-{Guid.NewGuid():N}";
        var protectedBasePath = $"/{protectedMarker}/v1";
        await using var controlServer = new SingleRequestLoopbackServer(HttpStatusCode.NoContent);
        await using var protectedServer = new SingleRequestLoopbackServer(
            responseBody:
            """
            {"choices":[{"message":{"content":"OK"},"finish_reason":"stop"}],"usage":{"total_tokens":1}}
            """);
        using var serviceProvider = BuildServiceProvider(
            openAiBaseUrl: protectedServer.BuildUri(protectedBasePath).AbsoluteUri);
        using var scope = serviceProvider.CreateScope();
        using var controlClient = serviceProvider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(ControlClientName);
        var provider = scope.ServiceProvider.GetRequiredService<OpenAiLlmProvider>();

        using var controlResponse = await controlClient.GetAsync(
            controlServer.BuildUri($"/control?marker={controlMarker}"));
        var result = await provider.CompleteAsync(new ChatCompletionRequest(
            [new ChatCompletionMessage("User", "registered provider")],
            SystemPrompt: string.Empty));
        var protectedWireRequest = await protectedServer.ReceivedRequest;

        controlResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        result.IsDegraded.Should().BeFalse();
        protectedWireRequest.Should().StartWith(
            $"POST {protectedBasePath}/chat/completions HTTP/1.1",
            "the protected handler must restore the configured origin only inside the registered transport chain");
        eventListener.Payloads.Should().Contain(
            payload => payload.Contains(controlMarker, StringComparison.Ordinal),
            "the control request must prove System.Net.Http EventSource capture is active");
        eventListener.Payloads.Should().NotContain(
            payload => payload.Contains(protectedMarker, StringComparison.Ordinal),
            "the provider itself must prepare the request before HttpClient emits RequestStart");
    }

    [Fact]
    public async Task EnabledSentry_ShouldExcludeOnlyProtectedClients()
    {
        var sentryTransport = new RecordingSentryTransport();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(SentryRegistration).Assembly.GetName().Name,
            EnvironmentName = "Testing"
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Llm:EnableLiveProviders"] = "true",
            ["Llm:AllowLiveProvidersInDevelopment"] = "true",
            ["Llm:Provider"] = "OpenAi",
            ["Llm:OpenAi:ApiKey"] = "test-openai-key",
            ["Llm:OpenAi:BaseUrl"] = "http://localhost:12345",
            ["Llm:OpenAi:Model"] = "test-openai-model",
            ["Llm:Ollama:AllowLocalhostEndpoints"] = "true",
            ["OutboundWebhooks:Security:AllowLocalhostEndpoints"] = "true"
        });
        builder.AddTaskdeckSentry(new SentrySettings
        {
            Enabled = true,
            Dsn = "https://public@example.invalid/1",
            Environment = "testing",
            TracesSampleRate = 1.0
        });
        builder.Services.PostConfigure<SentryAspNetCoreOptions>(
            options => options.Transport = sentryTransport);
        builder.Services.AddLlmProviders(builder.Configuration);
        builder.Services.AddTaskdeckWorkers(builder.Configuration, builder.Environment);
        builder.Services.AddHttpClient<GitHubConnectorProvider>()
            .ConfigurePrimaryHttpMessageHandler(BuildControlPrimaryHandler);

        await using var app = builder.Build();
        IHub? hub = null;
        ITransactionTracer? transaction = null;
        try
        {
            var sentryOptions = app.Services
                .GetRequiredService<IOptions<SentryAspNetCoreOptions>>()
                .Value;
            var handlerFactory = app.Services.GetRequiredService<IHttpMessageHandlerFactory>();
            var clientFactory = app.Services.GetRequiredService<IHttpClientFactory>();
            hub = app.Services.GetRequiredService<IHub>();
            var protectedClientNames = new[]
            {
                nameof(OpenAiLlmProvider),
                nameof(GeminiLlmProvider),
                nameof(OllamaLlmProvider),
                "OutboundWebhookDelivery"
            };
            var marker = $"sentry-protected-{Guid.NewGuid():N}";
            var controlMarker = $"sentry-control-{Guid.NewGuid():N}";

            sentryOptions.DisableSentryHttpMessageHandler.Should().BeFalse(
                "unrelated named and typed clients must retain Sentry's normal outgoing-request instrumentation");
            sentryOptions.Transport.Should().BeSameAs(
                sentryTransport,
                "the regression must flush Sentry envelopes without DNS, proxy, or network access");

            using var sentryScope = hub.PushScope();
            transaction = hub.StartTransaction("protected-outbound-test", "http.client");
            hub.ConfigureScope(scope => scope.Transaction = transaction);

            var controlHandlerTypes = EnumerateHandlerChain(
                    handlerFactory.CreateHandler(nameof(GitHubConnectorProvider)))
                .Select(handler => handler.GetType())
                .ToArray();
            controlHandlerTypes.Should().Contain(
                type => typeof(SentryHttpMessageHandler).IsAssignableFrom(type),
                "the real nonprotected GitHub typed-client name must retain Sentry instrumentation");

            await using (var controlServer = new SingleRequestLoopbackServer(HttpStatusCode.NoContent))
            using (var controlClient = clientFactory.CreateClient(nameof(GitHubConnectorProvider)))
            using (var controlResponse = await controlClient.GetAsync(
                       controlServer.BuildUri($"/control?marker={controlMarker}")))
            {
                var rawControlRequest = await controlServer.ReceivedRequest;
                controlResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
                HasHeader(rawControlRequest, "sentry-trace").Should().BeTrue(
                    "the nonprotected GitHub client must retain Sentry trace propagation");
            }

            foreach (var clientName in protectedClientNames)
            {
                var handlerTypes = EnumerateHandlerChain(handlerFactory.CreateHandler(clientName))
                    .Select(handler => handler.GetType())
                    .ToArray();
                handlerTypes.Should().NotContain(
                    type => typeof(SentryHttpMessageHandler).IsAssignableFrom(type),
                    $"Sentry must not observe URLs, failures, or headers from protected client {clientName}");

                foreach (var statusCode in new[] { HttpStatusCode.NoContent, HttpStatusCode.InternalServerError })
                {
                    await using var server = new SingleRequestLoopbackServer(statusCode);
                    using var client = clientFactory.CreateClient(clientName);
                    using var request = new HttpRequestMessage(
                        HttpMethod.Get,
                        server.BuildUri($"/protected?marker={marker}"));
                    ProtectedOutboundTelemetryHandler.PrepareForSend(request);
                    using var response = await client.SendAsync(request);
                    var rawRequest = await server.ReceivedRequest;

                    response.StatusCode.Should().Be(statusCode);
                    HasHeader(rawRequest, "sentry-trace").Should().BeFalse(
                        $"Sentry trace context must not leave protected client {clientName}");
                    HasHeader(rawRequest, "baggage").Should().BeFalse(
                        $"Sentry baggage must not leave protected client {clientName}");
                }
            }

            IReadOnlyCollection<Breadcrumb> breadcrumbs = Array.Empty<Breadcrumb>();
            hub.ConfigureScope(scope => breadcrumbs = scope.Breadcrumbs.ToArray());
            breadcrumbs.Should().NotContain(
                breadcrumb => BreadcrumbContains(breadcrumb, marker),
                "successful and failed protected requests must not create Sentry URL breadcrumbs");
        }
        finally
        {
            try
            {
                transaction?.Finish();
                hub?.ConfigureScope(scope => scope.Transaction = null);
            }
            finally
            {
                // Resolving IHub installs process-global Sentry state. This test does not start
                // the host, so ApplicationStopped cannot close it for subsequent API tests.
                SentrySdk.Close();
                SentrySdk.IsEnabled.Should().BeFalse(
                    "the enabled-Sentry regression must not leak its global hub into later API tests");
            }
        }

        sentryTransport.SentItemTypes.Should().ContainSingle(
            itemType => string.Equals(itemType, "transaction", StringComparison.Ordinal),
            "finishing the sampled transaction must flush it through the in-memory transport");
    }

    private static ServiceProvider BuildServiceProvider(
        BaseExporter<Metric>? metricExporter = null,
        string? openAiBaseUrl = null)
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
                ["Llm:OpenAi:BaseUrl"] = openAiBaseUrl ?? "http://localhost:12345",
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

    private static IEnumerable<HttpMessageHandler> EnumerateHandlerChain(HttpMessageHandler handler)
    {
        for (var current = handler; current is not null; current = (current as DelegatingHandler)?.InnerHandler)
        {
            yield return current;
        }
    }

    private static bool BreadcrumbContains(Breadcrumb breadcrumb, string marker) =>
        (breadcrumb.Message?.Contains(marker, StringComparison.Ordinal) ?? false) ||
        (breadcrumb.Data?.Any(pair =>
            pair.Key.Contains(marker, StringComparison.Ordinal) ||
            pair.Value?.ToString()?.Contains(marker, StringComparison.Ordinal) == true) ?? false);

    private sealed class RecordingHttpEventListener : EventListener
    {
        private ConcurrentQueue<string>? _payloads;

        internal RecordingHttpEventListener()
        {
            _payloads = new ConcurrentQueue<string>();
        }

        internal IReadOnlyCollection<string> Payloads => _payloads?.ToArray() ?? [];

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (string.Equals(eventSource.Name, "System.Net.Http", StringComparison.Ordinal))
            {
                EnableEvents(eventSource, EventLevel.Verbose, EventKeywords.All);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            var payloads = _payloads;
            if (payloads is null ||
                !string.Equals(eventData.EventSource.Name, "System.Net.Http", StringComparison.Ordinal))
            {
                return;
            }

            var values = eventData.Payload is null
                ? string.Empty
                : string.Join(
                    "|",
                    eventData.Payload.Select(value =>
                        Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty));
            payloads.Enqueue($"{eventData.EventName}|{values}");
        }
    }

    private sealed class RecordingSentryTransport : ITransport
    {
        private readonly ConcurrentQueue<string?> _sentItemTypes = new();

        internal IReadOnlyCollection<string?> SentItemTypes => _sentItemTypes.ToArray();

        public Task SendEnvelopeAsync(Envelope envelope, CancellationToken cancellationToken)
        {
            foreach (var item in envelope.Items)
            {
                _sentItemTypes.Enqueue(item.TryGetType());
            }

            return Task.CompletedTask;
        }
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
