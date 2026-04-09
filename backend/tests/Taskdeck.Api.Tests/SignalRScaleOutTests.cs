using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Taskdeck.Api.Extensions;
using Taskdeck.Api.Health;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Tests.Support;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Tests for SignalR scale-out readiness: conditional Redis backplane
/// registration, health check behavior, and in-memory fallback preservation.
/// </summary>
public class SignalRScaleOutTests
{
    // ── SignalRRegistration unit tests ──────────────────────────────────────

    [Fact]
    public void IsRedisBackplaneConfigured_ReturnsFalse_WhenNoConnectionString()
    {
        var config = BuildConfig(new Dictionary<string, string?>());
        SignalRRegistration.IsRedisBackplaneConfigured(config).Should().BeFalse();
    }

    [Fact]
    public void IsRedisBackplaneConfigured_ReturnsFalse_WhenEmptyConnectionString()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["SignalR:Redis:ConnectionString"] = ""
        });
        SignalRRegistration.IsRedisBackplaneConfigured(config).Should().BeFalse();
    }

    [Fact]
    public void IsRedisBackplaneConfigured_ReturnsFalse_WhenWhitespaceConnectionString()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["SignalR:Redis:ConnectionString"] = "   "
        });
        SignalRRegistration.IsRedisBackplaneConfigured(config).Should().BeFalse();
    }

    [Fact]
    public void IsRedisBackplaneConfigured_ReturnsTrue_WhenConnectionStringPresent()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["SignalR:Redis:ConnectionString"] = "localhost:6379"
        });
        SignalRRegistration.IsRedisBackplaneConfigured(config).Should().BeTrue();
    }

    [Fact]
    public void AddTaskdeckSignalR_WithoutRedis_LogsInMemoryMessage()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?>());
        var logger = new InMemoryLogger<object>();

        SignalRRegistration.AddTaskdeckSignalR(services, config, logger);

        logger.Entries.Should().ContainSingle(e =>
            e.Message.Contains("in-memory transport"));
    }

    [Fact]
    public void AddTaskdeckSignalR_WithRedis_LogsBackplaneEnabledMessage()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["SignalR:Redis:ConnectionString"] = "localhost:6379"
        });
        var logger = new InMemoryLogger<object>();

        // This will fail to actually connect to Redis, but the registration
        // should still succeed and log the configuration message.
        SignalRRegistration.AddTaskdeckSignalR(services, config, logger);

        logger.Entries.Should().ContainSingle(e =>
            e.Message.Contains("Redis backplane enabled"));
    }

    [Fact]
    public void AddTaskdeckSignalR_WithoutRedis_RegistersSignalRServices()
    {
        var services = new ServiceCollection();
        var config = BuildConfig(new Dictionary<string, string?>());
        var logger = new InMemoryLogger<object>();

        SignalRRegistration.AddTaskdeckSignalR(services, config, logger);

        // Verify core SignalR services are registered
        services.Should().Contain(sd =>
            sd.ServiceType.FullName != null &&
            sd.ServiceType.FullName.Contains("SignalR"));
    }

    // ── RedisBackplaneHealthCheck unit tests ───────────────────────────────

    [Fact]
    public void HealthCheck_IsConfigured_ReturnsFalse_WhenNoConnectionString()
    {
        var config = BuildConfig(new Dictionary<string, string?>());
        var logger = new InMemoryLogger<RedisBackplaneHealthCheck>();
        var healthCheck = new RedisBackplaneHealthCheck(config, logger);

        healthCheck.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void HealthCheck_IsConfigured_ReturnsTrue_WhenConnectionStringPresent()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["SignalR:Redis:ConnectionString"] = "localhost:6379"
        });
        var logger = new InMemoryLogger<RedisBackplaneHealthCheck>();
        var healthCheck = new RedisBackplaneHealthCheck(config, logger);

        healthCheck.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public async Task HealthCheck_ReturnsNotConfigured_WhenNoConnectionString()
    {
        var config = BuildConfig(new Dictionary<string, string?>());
        var logger = new InMemoryLogger<RedisBackplaneHealthCheck>();
        using var healthCheck = new RedisBackplaneHealthCheck(config, logger);

        var result = await healthCheck.CheckAsync();

        result.Status.Should().Be("NotConfigured");
        result.Error.Should().BeNull();
        result.LatencyMs.Should().BeNull();
    }

    [Fact(Timeout = 10_000)]
    public async Task HealthCheck_ReturnsUnhealthy_WhenRedisUnreachable()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            // localhost port 1 returns immediate connection refused on all platforms,
            // while non-routable IPs (e.g. 192.0.2.1) hang on Linux CI.
            ["SignalR:Redis:ConnectionString"] = "127.0.0.1:1,connectTimeout=1000,abortConnect=True"
        });
        var logger = new InMemoryLogger<RedisBackplaneHealthCheck>();
        using var healthCheck = new RedisBackplaneHealthCheck(config, logger);

        var result = await healthCheck.CheckAsync();

        result.Status.Should().Be("Unhealthy");
        result.Error.Should().NotBeNullOrEmpty();
        result.LatencyMs.Should().BeNull();
    }

    // ── Integration tests (in-memory mode) ─────────────────────────────────

    [Fact]
    public async Task ReadyEndpoint_IncludesSignalrBackplaneCheck_InMemoryMode()
    {
        await using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        (response.StatusCode == HttpStatusCode.OK ||
         response.StatusCode == HttpStatusCode.ServiceUnavailable)
            .Should().BeTrue();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var checks = payload.GetProperty("checks");
        checks.TryGetProperty("signalrBackplane", out var backplane).Should().BeTrue();
        backplane.GetProperty("status").GetString().Should().Be("NotConfigured");
    }

    [Fact]
    public async Task SignalRHub_WorksInMemoryMode_WithoutRedis()
    {
        // Verify the existing hub endpoint is still mapped and accessible
        // when running without Redis (in-memory mode).
        await using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient();

        // The hub negotiate endpoint should return 200 or redirect for
        // authenticated users. Unauthenticated requests should get 401.
        var response = await client.PostAsync("/hubs/boards/negotiate?negotiateVersion=1", null);

        // 401 is expected for unauthenticated negotiate — the hub is mapped and
        // the [Authorize] attribute is working correctly.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SignalRHub_NegotiateWorks_ForAuthenticatedUser_InMemoryMode()
    {
        await using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "signalr-test-user");

        var response = await client.PostAsync("/hubs/boards/negotiate?negotiateVersion=1", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.TryGetProperty("connectionToken", out _).Should().BeTrue();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static IConfiguration BuildConfig(Dictionary<string, string?> overrides)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(overrides)
            .Build();
    }
}
