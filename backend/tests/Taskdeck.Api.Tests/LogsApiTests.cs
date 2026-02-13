using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests;

public class LogsApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public LogsApiTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task QueryLogs_ShouldReturnCommandRunEntries()
    {
        await AuthenticateAsync("logs-query");
        await _client.PostAsJsonAsync("/api/ops/cli/run", new RunCommandDto("health.check"));

        var response = await _client.GetAsync("/api/logs?source=OpsCliService&limit=50");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var entries = await response.Content.ReadFromJsonAsync<List<LogEntryDto>>();
        entries.Should().NotBeNull();
        entries.Should().Contain(entry => entry.Source == "OpsCliService");
    }

    [Fact]
    public async Task CorrelationLookup_ShouldReturnRunLogs()
    {
        await AuthenticateAsync("logs-correlation");
        var runResponse = await _client.PostAsJsonAsync("/api/ops/cli/run", new RunCommandDto("health.check"));
        runResponse.EnsureSuccessStatusCode();
        var run = await runResponse.Content.ReadFromJsonAsync<CommandRunDto>();
        run.Should().NotBeNull();

        var correlationResponse = await _client.GetAsync($"/api/logs/correlation/{run!.CorrelationId}");
        correlationResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var entries = await correlationResponse.Content.ReadFromJsonAsync<List<LogEntryDto>>();
        entries.Should().NotBeNull();
        entries.Should().Contain(entry => entry.CorrelationId == run.CorrelationId);
    }

    [Fact]
    public async Task QueryLogs_ShouldReturnBadRequest_ForInvalidLimit()
    {
        await AuthenticateAsync("logs-validation");

        var response = await _client.GetAsync("/api/logs?limit=0");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<Guid> AuthenticateAsync(string stem)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var username = $"{stem}_{suffix}";
        var email = $"{stem}_{suffix}@example.com";
        const string password = "password123";

        var response = await _client.PostAsJsonAsync(
            "/api/auth/register",
            new CreateUserDto(username, email, password));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<AuthResultDto>();
        payload.Should().NotBeNull();

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.Token);
        return payload.User.Id;
    }
}
