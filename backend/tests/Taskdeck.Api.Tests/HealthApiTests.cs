using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
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
    private const string DevRunIdConfigKey = "TASKDECK_DEV_RUN_ID";
    private const string DevRunIdHeaderName = "Taskdeck-Dev-Run-Id";

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
    public async Task Ready_ShouldEmitCanonicalDevelopmentRunIdentityWithoutLeakingItInTheBody()
    {
        const string configuredRunId = "A87D0D31-AE09-405C-8DCE-75289FBA8F15";
        var expectedRunId = Guid.Parse(configuredRunId).ToString("D");
        using var factory = CreateFactoryWithDevRunId(configuredRunId);
        using var client = factory.CreateClient();

        var readyResponse = await client.GetAsync("/health/ready");

        readyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        readyResponse.Headers.TryGetValues(DevRunIdHeaderName, out var runIdValues).Should().BeTrue();
        runIdValues.Should().ContainSingle().Which.Should().Be(expectedRunId);
        readyResponse.Headers.CacheControl.Should().NotBeNull();
        readyResponse.Headers.CacheControl!.NoStore.Should().BeTrue();
        var body = await readyResponse.Content.ReadAsStringAsync();
        body.Contains(expectedRunId, StringComparison.OrdinalIgnoreCase).Should().BeFalse();

        var liveResponse = await client.GetAsync("/health/live");
        liveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        liveResponse.Headers.TryGetValues(DevRunIdHeaderName, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("a87d0d31ae09405c8dce75289fba8f15")]
    [InlineData("{a87d0d31-ae09-405c-8dce-75289fba8f15}")]
    public async Task Ready_ShouldSuppressDevelopmentRunIdentityWhenConfigurationIsAbsentOrInvalid(
        string? configuredRunId)
    {
        using var factory = CreateFactoryWithDevRunId(configuredRunId);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues(DevRunIdHeaderName, out _).Should().BeFalse();
    }

    [Fact]
    public async Task Ready_ShouldSuppressDevelopmentRunIdentityOutsideDevelopment()
    {
        const string configuredRunId = "a87d0d31-ae09-405c-8dce-75289fba8f15";
        using var factory = CreateFactoryWithDevRunId(configuredRunId, "Production");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues(DevRunIdHeaderName, out _).Should().BeFalse();
        var body = await response.Content.ReadAsStringAsync();
        body.Contains(configuredRunId, StringComparison.OrdinalIgnoreCase).Should().BeFalse();
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
            OpenAiCompatible = new OpenAiCompatibleProviderSettings
            {
                ApiKey = "test-key",
                BaseUrl = "https://api.groq.com/openai/v1",
                Model = "test-model",
                TimeoutSeconds = 300
            },
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

        providerSettings.Provider = "OpenAiCompatible";
        providerSettings.OpenAiCompatible.TimeoutSeconds = 77;
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

    private WebApplicationFactory<Program> CreateFactoryWithDevRunId(
        string? configuredRunId,
        string environmentName = "Development")
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environmentName);
            if (environmentName == "Production")
            {
                builder.UseSetting("Jwt:SecretKey", ApiTestHarness.ProductionTestJwtSecret);
                builder.UseSetting("Connectors:EncryptionKey", ApiTestHarness.TestEncryptionKey);
            }

            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [DevRunIdConfigKey] = configuredRunId
                });
            });
        });
    }
}
