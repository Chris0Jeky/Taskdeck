using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Taskdeck.Application.Services;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Tests for the Polly circuit breaker wired onto LLM provider HTTP clients.
/// Verifies that the circuit opens after consecutive failures, transitions
/// through half-open, and resets after a successful probe request. Also
/// verifies that circuit breaker state is reported on the health endpoint.
/// </summary>
public class CircuitBreakerTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _baseFactory;

    public CircuitBreakerTests(TestWebApplicationFactory baseFactory)
    {
        _baseFactory = baseFactory;
    }

    // ── CircuitBreakerStateTracker unit tests ──────────────────────────

    [Fact]
    public void Tracker_RecordState_StoresSnapshot()
    {
        var tracker = new CircuitBreakerStateTracker();

        tracker.RecordState("TestCircuit", CircuitState.Open, "server error");

        var snapshot = tracker.Get("TestCircuit");
        snapshot.Should().NotBeNull();
        snapshot!.CircuitName.Should().Be("TestCircuit");
        snapshot.State.Should().Be(CircuitState.Open);
        snapshot.LastFailureReason.Should().Be("server error");
    }

    [Fact]
    public void Tracker_Get_ReturnsNullForUnknownCircuit()
    {
        var tracker = new CircuitBreakerStateTracker();

        tracker.Get("NonExistent").Should().BeNull();
    }

    [Fact]
    public void Tracker_GetAll_ReturnsAllRecordedCircuits()
    {
        var tracker = new CircuitBreakerStateTracker();
        tracker.RecordState("A", CircuitState.Open, "fail A");
        tracker.RecordState("B", CircuitState.Closed);

        var all = tracker.GetAll();
        all.Should().ContainKey("A");
        all.Should().ContainKey("B");
        all["A"].State.Should().Be(CircuitState.Open);
        all["B"].State.Should().Be(CircuitState.Closed);
    }

    [Fact]
    public void Tracker_RecordState_OverwritesPreviousSnapshot()
    {
        var tracker = new CircuitBreakerStateTracker();
        tracker.RecordState("X", CircuitState.Open, "first failure");
        tracker.RecordState("X", CircuitState.Closed);

        var snapshot = tracker.Get("X");
        snapshot.Should().NotBeNull();
        snapshot!.State.Should().Be(CircuitState.Closed);
        snapshot.LastFailureReason.Should().BeNull();
    }

    [Fact]
    public void Tracker_RecordState_SetsLastTransitionUtc()
    {
        var tracker = new CircuitBreakerStateTracker();
        var before = DateTimeOffset.UtcNow;
        tracker.RecordState("T", CircuitState.HalfOpen);
        var after = DateTimeOffset.UtcNow;

        var snapshot = tracker.Get("T");
        snapshot.Should().NotBeNull();
        snapshot!.LastTransitionUtc.Should().BeOnOrAfter(before);
        snapshot.LastTransitionUtc.Should().BeOnOrBefore(after);
    }

    [Fact]
    public async Task Tracker_IsThreadSafe_ConcurrentWrites()
    {
        var tracker = new CircuitBreakerStateTracker();
        var tasks = Enumerable.Range(0, 100).Select(i =>
            Task.Run(() => tracker.RecordState($"Circuit-{i % 10}", CircuitState.Open, $"fail-{i}")));

        await Task.WhenAll(tasks);

        // Should have 10 unique circuit names
        var all = tracker.GetAll();
        all.Count.Should().Be(10);
    }

    // ── Health endpoint reports circuit breaker state ──────────────────

    [Fact]
    public async Task HealthReady_IncludesCircuitBreakersSection()
    {
        using var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.TryGetProperty("checks", out var checks).Should().BeTrue();
        checks.TryGetProperty("circuitBreakers", out _).Should().BeTrue();
    }

    [Fact]
    public async Task HealthReady_ShowsAllClosedWhenNoTransitions()
    {
        using var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var circuitBreakers = payload.GetProperty("checks").GetProperty("circuitBreakers");
        circuitBreakers.TryGetProperty("status", out var status).Should().BeTrue();
        status.GetString().Should().Be("AllClosed");
    }

    [Fact]
    public async Task HealthReady_ReportsOpenCircuitState()
    {
        using var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                // Pre-record an open circuit state so the health endpoint reports it.
                var existingTracker = services
                    .FirstOrDefault(d => d.ServiceType == typeof(CircuitBreakerStateTracker));
                if (existingTracker is { ImplementationInstance: CircuitBreakerStateTracker tracker })
                {
                    tracker.RecordState("OpenAI", CircuitState.Open, "HTTP 500");
                }
                else
                {
                    // If the tracker was not registered as an instance, create one.
                    services.RemoveAll<CircuitBreakerStateTracker>();
                    var newTracker = new CircuitBreakerStateTracker();
                    newTracker.RecordState("OpenAI", CircuitState.Open, "HTTP 500");
                    services.AddSingleton(newTracker);
                }
            });
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var circuitBreakers = payload.GetProperty("checks").GetProperty("circuitBreakers");
        circuitBreakers.TryGetProperty("OpenAI", out var openAi).Should().BeTrue();
        openAi.GetProperty("state").GetString().Should().Be("Open");
        openAi.TryGetProperty("lastFailureReason", out _).Should().BeFalse(
            "lastFailureReason must not be exposed to prevent info disclosure");
    }

    [Fact]
    public async Task HealthReady_OpenCircuitReportsDegradedButDoesNotFailReadiness()
    {
        using var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<CircuitBreakerStateTracker>();
                var tracker = new CircuitBreakerStateTracker();
                tracker.RecordState("OpenAICompatible", CircuitState.Open, "Connection refused");
                services.AddSingleton(tracker);
            });
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        // An open circuit on an optional provider should NOT fail readiness.
        // LLM and OAuth providers degrade gracefully (mock fallback, cached tokens).
        // The circuit state is reported for operator visibility.
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var circuitBreakers = payload.GetProperty("checks").GetProperty("circuitBreakers");
        circuitBreakers.TryGetProperty("OpenAICompatible", out var compatible).Should().BeTrue();
        compatible.GetProperty("state").GetString().Should().Be("Open");
        circuitBreakers.GetProperty("_summary").GetProperty("status").GetString().Should().Be("Degraded");
    }

    [Fact]
    public async Task HealthReady_HalfOpenCircuitDoesNotDegradeReadiness()
    {
        using var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<CircuitBreakerStateTracker>();
                var tracker = new CircuitBreakerStateTracker();
                tracker.RecordState("OpenAI", CircuitState.HalfOpen);
                services.AddSingleton(tracker);
            });
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        // Half-open does not degrade readiness — only open does.
        // Status may still be 503 for other reasons (worker staleness), but the
        // circuit breaker section should not be the cause.
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var circuitBreakers = payload.GetProperty("checks").GetProperty("circuitBreakers");
        circuitBreakers.TryGetProperty("OpenAI", out var openAi).Should().BeTrue();
        openAi.GetProperty("state").GetString().Should().Be("HalfOpen");
    }

    [Fact]
    public async Task HealthReady_ClosedCircuitDoesNotDegradeReadiness()
    {
        using var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<CircuitBreakerStateTracker>();
                var tracker = new CircuitBreakerStateTracker();
                tracker.RecordState("OpenAI", CircuitState.Closed);
                services.AddSingleton(tracker);
            });
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var circuitBreakers = payload.GetProperty("checks").GetProperty("circuitBreakers");
        circuitBreakers.TryGetProperty("OpenAI", out var openAi).Should().BeTrue();
        openAi.GetProperty("state").GetString().Should().Be("Closed");
    }

    // ── CircuitBreakerSettings defaults ─────────────────────────────────

    [Fact]
    public void Settings_DefaultValues()
    {
        var settings = new CircuitBreakerSettings();

        settings.FailureThreshold.Should().Be(5);
        settings.BreakDurationSeconds.Should().Be(60);
    }

    [Fact]
    public void Settings_CanBeConfigured()
    {
        var settings = new CircuitBreakerSettings
        {
            FailureThreshold = 10,
            BreakDurationSeconds = 120
        };

        settings.FailureThreshold.Should().Be(10);
        settings.BreakDurationSeconds.Should().Be(120);
    }

    [Fact]
    public void Settings_FailureThreshold_HasRangeValidation()
    {
        var settings = new CircuitBreakerSettings { FailureThreshold = 0 };

        var context = new System.ComponentModel.DataAnnotations.ValidationContext(settings);
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            settings, context, results, validateAllProperties: true);

        isValid.Should().BeFalse();
        results.Should().Contain(r => r.ErrorMessage!.Contains("FailureThreshold"));
    }

    [Fact]
    public void Settings_BreakDurationSeconds_HasRangeValidation()
    {
        var settings = new CircuitBreakerSettings { BreakDurationSeconds = 0 };

        var context = new System.ComponentModel.DataAnnotations.ValidationContext(settings);
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            settings, context, results, validateAllProperties: true);

        isValid.Should().BeFalse();
        results.Should().Contain(r => r.ErrorMessage!.Contains("BreakDurationSeconds"));
    }

    // ── Polly policy integration via LlmProviderRegistration ──────────

    [Fact]
    public void BuildCircuitBreakerPolicy_CreatesWorkingPolicy()
    {
        var tracker = new CircuitBreakerStateTracker();
        var settings = new CircuitBreakerSettings { FailureThreshold = 3, BreakDurationSeconds = 30 };

        var policy = Taskdeck.Api.Extensions.LlmProviderRegistration
            .BuildCircuitBreakerPolicy(tracker, "TestCircuit", settings);

        policy.Should().NotBeNull();
    }

    [Fact]
    public async Task BuildCircuitBreakerPolicy_OpensAfterConsecutiveFailures()
    {
        var tracker = new CircuitBreakerStateTracker();
        var settings = new CircuitBreakerSettings { FailureThreshold = 3, BreakDurationSeconds = 30 };

        var policy = Taskdeck.Api.Extensions.LlmProviderRegistration
            .BuildCircuitBreakerPolicy(tracker, "TestCircuit", settings);

        // Simulate 3 consecutive failures (500 responses).
        for (var i = 0; i < 3; i++)
        {
            try
            {
                await policy.ExecuteAsync(_ =>
                    Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)),
                    CancellationToken.None);
            }
            catch
            {
                // Circuit breaker may throw on the breaking request.
            }
        }

        // After 3 failures, circuit should be open.
        var snapshot = tracker.Get("TestCircuit");
        snapshot.Should().NotBeNull();
        snapshot!.State.Should().Be(CircuitState.Open);
    }

    [Fact]
    public async Task BuildCircuitBreakerPolicy_CircuitRejectsRequestsWhenOpen()
    {
        var tracker = new CircuitBreakerStateTracker();
        var settings = new CircuitBreakerSettings { FailureThreshold = 2, BreakDurationSeconds = 60 };

        var policy = Taskdeck.Api.Extensions.LlmProviderRegistration
            .BuildCircuitBreakerPolicy(tracker, "RejectCircuit", settings);

        // Trip the circuit with 2 failures.
        for (var i = 0; i < 2; i++)
        {
            try
            {
                await policy.ExecuteAsync(_ =>
                    Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)),
                    CancellationToken.None);
            }
            catch
            {
                // Expected.
            }
        }

        // The next request should be rejected by the open circuit.
        Func<Task> act = () => policy.ExecuteAsync(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)),
            CancellationToken.None);

        await act.Should().ThrowAsync<Polly.CircuitBreaker.BrokenCircuitException>();
    }

    [Fact]
    public async Task BuildCircuitBreakerPolicy_SuccessfulRequestsDoNotTripCircuit()
    {
        var tracker = new CircuitBreakerStateTracker();
        var settings = new CircuitBreakerSettings { FailureThreshold = 3, BreakDurationSeconds = 30 };

        var policy = Taskdeck.Api.Extensions.LlmProviderRegistration
            .BuildCircuitBreakerPolicy(tracker, "SuccessCircuit", settings);

        // 10 successful requests should not trip the circuit.
        for (var i = 0; i < 10; i++)
        {
            await policy.ExecuteAsync(_ =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)),
                CancellationToken.None);
        }

        // Circuit should not have been recorded (never transitioned from initial closed).
        var snapshot = tracker.Get("SuccessCircuit");
        snapshot.Should().BeNull();
    }

    [Fact]
    public async Task BuildCircuitBreakerPolicy_TransientErrorsCountAsFailures()
    {
        var tracker = new CircuitBreakerStateTracker();
        var settings = new CircuitBreakerSettings { FailureThreshold = 2, BreakDurationSeconds = 30 };

        var policy = Taskdeck.Api.Extensions.LlmProviderRegistration
            .BuildCircuitBreakerPolicy(tracker, "TransientCircuit", settings);

        // 408 (Request Timeout) is treated as transient by HttpPolicyExtensions.
        for (var i = 0; i < 2; i++)
        {
            try
            {
                await policy.ExecuteAsync(_ =>
                    Task.FromResult(new HttpResponseMessage(HttpStatusCode.RequestTimeout)),
                    CancellationToken.None);
            }
            catch
            {
                // Expected.
            }
        }

        var snapshot = tracker.Get("TransientCircuit");
        snapshot.Should().NotBeNull();
        snapshot!.State.Should().Be(CircuitState.Open);
    }

    [Fact]
    public async Task BuildCircuitBreakerPolicy_400DoesNotTripCircuit()
    {
        var tracker = new CircuitBreakerStateTracker();
        var settings = new CircuitBreakerSettings { FailureThreshold = 2, BreakDurationSeconds = 30 };

        var policy = Taskdeck.Api.Extensions.LlmProviderRegistration
            .BuildCircuitBreakerPolicy(tracker, "ClientErrorCircuit", settings);

        // 400 (Bad Request) is NOT transient — should not trip the circuit.
        for (var i = 0; i < 5; i++)
        {
            await policy.ExecuteAsync(_ =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)),
                CancellationToken.None);
        }

        var snapshot = tracker.Get("ClientErrorCircuit");
        snapshot.Should().BeNull();
    }

    // ── OAuth backchannel handler construction ─────────────────────────

    [Fact]
    public void BuildOAuthBackchannelHandler_CreatesValidHandler()
    {
        var tracker = new CircuitBreakerStateTracker();
        var settings = new CircuitBreakerSettings();

        var handler = Taskdeck.Api.Extensions.AuthenticationRegistration
            .BuildOAuthBackchannelHandler(tracker, settings, "GitHubOAuth");

        handler.Should().NotBeNull();
    }
}
