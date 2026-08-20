using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Controllers;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.Common;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Api.Tests;

public class HealthApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public HealthApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Live_ShouldReturnHealthy()
    {
        var response = await _client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("status").GetString().Should().Be("Healthy");
    }

    [Fact]
    public async Task Live_ShouldReportTheStampedProductVersion()
    {
        var response = await _client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.TryGetProperty("version", out var version).Should().BeTrue(
            "a self-hoster must be able to answer 'what version am I running?' (#1804)");
        version.GetString().Should().Be(ProductVersion.Value);
        version.GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Ready_ShouldReportTheStampedProductVersion()
    {
        var response = await _client.GetAsync("/health/ready");

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.TryGetProperty("version", out var version).Should().BeTrue();
        version.GetString().Should().Be(ProductVersion.Value);
    }

    [Fact]
    public async Task Ready_ShouldReturnPayloadWithChecks()
    {
        var response = await _client.GetAsync("/health/ready");

        (response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.ServiceUnavailable)
            .Should()
            .BeTrue();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.TryGetProperty("checks", out var checks).Should().BeTrue();
        checks.TryGetProperty("database", out _).Should().BeTrue();
        checks.TryGetProperty("queue", out _).Should().BeTrue();
        checks.TryGetProperty("workers", out _).Should().BeTrue();

        var workers = checks.GetProperty("workers");
        var queueWorker = workers.GetProperty("queueToProposal");
        queueWorker.TryGetProperty("stalenessSeconds", out _).Should().BeTrue();
        queueWorker.TryGetProperty("maxStalenessSeconds", out _).Should().BeTrue();

        var housekeepingWorker = workers.GetProperty("proposalHousekeeping");
        housekeepingWorker.TryGetProperty("stalenessSeconds", out _).Should().BeTrue();
        housekeepingWorker.TryGetProperty("maxStalenessSeconds", out _).Should().BeTrue();

        // Transcript lane (REVIVAL-08): monitored with its own, much larger staleness budget
        // because one tick legitimately blocks for minutes of sequential LLM calls.
        var transcriptWorker = workers.GetProperty("transcriptTriage");
        transcriptWorker.TryGetProperty("stalenessSeconds", out _).Should().BeTrue();
        transcriptWorker.TryGetProperty("maxStalenessSeconds", out var transcriptMax).Should().BeTrue();
        var queueMax = queueWorker.GetProperty("maxStalenessSeconds").GetDouble();
        transcriptMax.GetDouble().Should().BeGreaterThan(queueMax);
        var expectedTranscriptMax = HealthController.CalculateTranscriptWorkerMaxStaleness(
            _factory.Services.GetRequiredService<WorkerSettings>(),
            _factory.Services.GetRequiredService<LlmProviderSettings>(),
            _factory.Services.GetRequiredService<IWebHostEnvironment>().EnvironmentName);
        transcriptMax.GetDouble().Should().Be(expectedTranscriptMax.TotalSeconds);
    }

    [Fact]
    public void CalculateTranscriptWorkerMaxStaleness_ShouldUseOnlyTheSelectedProviderTimeout()
    {
        var workerSettings = new WorkerSettings
        {
            QueuePollIntervalSeconds = 5,
            MaxBatchSize = 100
        };
        var providerSettings = new LlmProviderSettings
        {
            EnableLiveProviders = true,
            Provider = "OpenAi",
            OpenAi = new OpenAiProviderSettings { ApiKey = "test-key", TimeoutSeconds = 30 },
            Gemini = new GeminiProviderSettings { ApiKey = "test-key", TimeoutSeconds = 300 },
            Ollama = new OllamaProviderSettings { TimeoutSeconds = 600 }
        };

        HealthController.CalculateTranscriptWorkerMaxStaleness(workerSettings, providerSettings, "Production")
            .TotalSeconds.Should().Be(60);

        providerSettings.Provider = "ollama";
        providerSettings.AllowLiveProvidersInDevelopment = true;
        providerSettings.Ollama.AllowLocalhostEndpoints = true;
        providerSettings.Ollama.TimeoutSeconds = 120;
        HealthController.CalculateTranscriptWorkerMaxStaleness(workerSettings, providerSettings, "Development")
            .TotalSeconds.Should().Be(150);

        providerSettings.Provider = "Gemini";
        providerSettings.Gemini.TimeoutSeconds = 77;
        HealthController.CalculateTranscriptWorkerMaxStaleness(workerSettings, providerSettings, "Production")
            .TotalSeconds.Should().Be(107);

        providerSettings.Provider = "Ollama";
        providerSettings.EnableLiveProviders = false;
        providerSettings.Ollama.TimeoutSeconds = 600;
        HealthController.CalculateTranscriptWorkerMaxStaleness(workerSettings, providerSettings, "Production")
            .TotalSeconds.Should().Be(60);
    }

    [Fact]
    public void IsWorkerHeartbeatHealthy_ShouldHonorTheExactTranscriptBudgetBoundary()
    {
        var now = DateTimeOffset.UtcNow;
        var maxStaleness = TimeSpan.FromSeconds(150);
        var startupTime = now - TimeSpan.FromMinutes(1);

        HealthController.IsWorkerHeartbeatHealthy(now - maxStaleness, startupTime, maxStaleness, now)
            .Should().BeTrue();
        HealthController.IsWorkerHeartbeatHealthy(
                now - maxStaleness - TimeSpan.FromTicks(1),
                startupTime,
                maxStaleness,
                now)
            .Should().BeFalse();
    }

    [Fact]
    public async Task Ready_ShouldNotLeakExceptionDetails()
    {
        var response = await _client.GetAsync("/health/ready");
        (response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.ServiceUnavailable)
            .Should()
            .BeTrue();

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContainAny(
            "Exception", "StackTrace", "at System.", "at Microsoft.",
            "Data Source=", "Password=", "Server=");
    }

    [Fact]
    public async Task Ready_ShouldNotExposeCircuitBreakerFailureReasons()
    {
        var response = await _client.GetAsync("/health/ready");
        (response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.ServiceUnavailable)
            .Should()
            .BeTrue();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (payload.TryGetProperty("checks", out var checks) &&
            checks.TryGetProperty("circuitBreakers", out var cb) &&
            cb.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in cb.EnumerateObject())
            {
                if (prop.Name == "_summary" || prop.Name == "status") continue;
                if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                prop.Value.TryGetProperty("lastFailureReason", out _).Should().BeFalse(
                    "circuit breaker entries must not expose failure reason details");
            }
        }
    }

    [Fact]
    public async Task Ready_ShouldExcludeCaptureBacklogFromAutomationQueueDepth()
    {
        await ApiTestHarness.AuthenticateAsync(_client, "health-capture-backlog");

        for (var i = 0; i < 3; i++)
        {
            var captureResponse = await _client.PostAsJsonAsync(
                "/api/capture/items",
                new CreateCaptureItemDto(null, $"capture payload {i}"));
            captureResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var response = await _client.GetAsync("/health/ready");
        (response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.ServiceUnavailable)
            .Should()
            .BeTrue();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var queue = payload.GetProperty("checks").GetProperty("queue");
        queue.GetProperty("depth").GetInt32().Should().Be(0);
        queue.GetProperty("captureDepth").GetInt32().Should().BeGreaterThanOrEqualTo(3);
        queue.GetProperty("totalDepth").GetInt32().Should().BeGreaterThanOrEqualTo(3);
    }
}
