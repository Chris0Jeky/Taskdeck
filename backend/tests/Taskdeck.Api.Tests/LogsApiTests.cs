using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests;

public class LogsApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly TestWebApplicationFactory _factory;

    public LogsApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
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

    [Fact]
    public async Task QueryLogs_ShouldReturnUnauthorized_WhenNotAuthenticated()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.GetAsync("/api/logs?limit=10");
        await ApiTestHarness.AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task StreamLogs_ShouldReturnUnauthorized_WhenNotAuthenticated()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.GetAsync("/api/logs/stream");
        await ApiTestHarness.AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task CorrelationLookup_ShouldReturnUnauthorized_WhenNotAuthenticated()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.GetAsync($"/api/logs/correlation/{Guid.NewGuid():N}");
        await ApiTestHarness.AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task CorrelationLookup_ShouldReturnNotFound_WhenCorrelationIdDoesNotExist()
    {
        await AuthenticateAsync("logs-correlation-notfound");

        var response = await _client.GetAsync($"/api/logs/correlation/{Guid.NewGuid():N}");
        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound, "NotFound");
    }

    [Fact]
    public async Task CorrelationLookup_ShouldReturnForbidden_WhenCorrelationBelongsToDifferentUser()
    {
        using var ownerClient = _factory.CreateClient();
        using var outsiderClient = _factory.CreateClient();

        await AuthenticateAsync(ownerClient, "logs-correlation-owner");
        await AuthenticateAsync(outsiderClient, "logs-correlation-outsider");

        var runResponse = await ownerClient.PostAsJsonAsync("/api/ops/cli/run", new RunCommandDto("health.check"));
        runResponse.EnsureSuccessStatusCode();
        var run = await runResponse.Content.ReadFromJsonAsync<CommandRunDto>();
        run.Should().NotBeNull();

        var response = await outsiderClient.GetAsync($"/api/logs/correlation/{run!.CorrelationId}");
        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    [Fact]
    public async Task QueryLogs_ShouldReturnForbidden_WhenRequestingDifferentUserId()
    {
        using var ownerClient = _factory.CreateClient();
        using var outsiderClient = _factory.CreateClient();

        var ownerUserId = await AuthenticateAsync(ownerClient, "logs-query-owner");
        await AuthenticateAsync(outsiderClient, "logs-query-outsider");

        var response = await outsiderClient.GetAsync($"/api/logs?userId={ownerUserId}");
        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    private async Task<Guid> AuthenticateAsync(string stem)
    {
        return await AuthenticateAsync(_client, stem);
    }

    private static async Task<Guid> AuthenticateAsync(HttpClient client, string stem)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var username = $"{stem}_{suffix}";
        var email = $"{stem}_{suffix}@example.com";
        const string password = "password123";

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new CreateUserDto(username, email, password));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<AuthResultDto>();
        payload.Should().NotBeNull();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.Token);
        return payload.User.Id;
    }
}
