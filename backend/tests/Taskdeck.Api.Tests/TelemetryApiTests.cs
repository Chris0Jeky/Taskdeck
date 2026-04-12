using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Xunit;

namespace Taskdeck.Api.Tests;

public class TelemetryApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public TelemetryApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetConfig_ShouldReturnOk_WithAllSectionsDisabled()
    {
        var response = await _client.GetAsync("/api/telemetry/config");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        var sentry = payload.GetProperty("sentry");
        sentry.GetProperty("enabled").GetBoolean().Should().BeFalse();
        sentry.GetProperty("dsn").GetString().Should().BeEmpty();

        var analytics = payload.GetProperty("analytics");
        analytics.GetProperty("enabled").GetBoolean().Should().BeFalse();

        var telemetry = payload.GetProperty("telemetry");
        telemetry.GetProperty("enabled").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task GetConfig_ShouldBeAccessibleWithoutAuth()
    {
        // Create a client without any auth headers
        var response = await _client.GetAsync("/api/telemetry/config");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PostEvents_ShouldRequireAuth()
    {
        // Create an unauthenticated client to verify the endpoint rejects
        // requests without auth headers. Using a fresh factory client ensures
        // no auth state from other tests leaks in.
        using var unauthClient = _factory.CreateClient();
        var response = await unauthClient.PostAsJsonAsync("/api/telemetry/events", new
        {
            events = new[]
            {
                new { @event = "capture.submitted", timestamp = "2026-04-09T12:00:00Z", sessionId = "abc", workspaceMode = "guided", appVersion = "0.1.0", platform = "web" }
            }
        });

        // Without auth header it should return 401
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostEvents_ShouldReturnZeroRecorded_WhenTelemetryDisabled()
    {
        await ApiTestHarness.AuthenticateAsync(_client, "telemetry_user");

        var response = await _client.PostAsJsonAsync("/api/telemetry/events", new
        {
            events = new[]
            {
                new { @event = "capture.submitted", timestamp = "2026-04-09T12:00:00Z", sessionId = "abc", workspaceMode = "guided", appVersion = "0.1.0", platform = "web" }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("recorded").GetInt32().Should().Be(0);
    }
}
