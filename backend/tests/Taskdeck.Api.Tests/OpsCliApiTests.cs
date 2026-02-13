using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Api.Tests;

public class OpsCliApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public OpsCliApiTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task RunCommand_ShouldExecuteHealthTemplate_ForEditor()
    {
        await AuthenticateAsync("ops-health");

        var response = await _client.PostAsJsonAsync(
            "/api/ops/cli/run",
            new RunCommandDto("health.check"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<CommandRunDto>();
        payload.Should().NotBeNull();
        payload!.Status.Should().Be(CommandRunStatus.Completed);
        payload.OutputPreview.Should().Contain("Health check: OK");
    }

    [Fact]
    public async Task RunCommand_ShouldReturnForbidden_WhenRoleIsInsufficient()
    {
        await AuthenticateAsync("ops-forbidden");

        var response = await _client.PostAsJsonAsync(
            "/api/ops/cli/run",
            new RunCommandDto("boards.list"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("errorCode").GetString().Should().Be("Forbidden");
    }

    [Fact]
    public async Task GetRun_ShouldReturnForbidden_ForDifferentUser()
    {
        await AuthenticateAsync("ops-owner");
        var runResponse = await _client.PostAsJsonAsync(
            "/api/ops/cli/run",
            new RunCommandDto("health.check"));
        runResponse.EnsureSuccessStatusCode();
        var run = await runResponse.Content.ReadFromJsonAsync<CommandRunDto>();
        run.Should().NotBeNull();

        await AuthenticateAsync("ops-other");
        var getResponse = await _client.GetAsync($"/api/ops/cli/runs/{run!.Id}");

        getResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RunCommand_ShouldReturnBadRequest_ForUnknownParameter()
    {
        await AuthenticateAsync("ops-params");

        var response = await _client.PostAsJsonAsync(
            "/api/ops/cli/run",
            new RunCommandDto("health.check", new Dictionary<string, string>
            {
                ["unexpected"] = "value"
            }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("errorCode").GetString().Should().Be("ValidationError");
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
