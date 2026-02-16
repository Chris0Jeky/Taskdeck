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

    [Fact]
    public async Task RunCommand_ShouldUseRequestCorrelationId_WhenProvided()
    {
        await AuthenticateAsync("ops-correlation");
        var requestCorrelationId = $"req-{Guid.NewGuid():N}";
        _client.DefaultRequestHeaders.Remove("X-Request-Id");
        _client.DefaultRequestHeaders.TryAddWithoutValidation("X-Request-Id", requestCorrelationId);

        try
        {
            var response = await _client.PostAsJsonAsync(
                "/api/ops/cli/run",
                new RunCommandDto("health.check"));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var payload = await response.Content.ReadFromJsonAsync<CommandRunDto>();
            payload.Should().NotBeNull();
            payload!.CorrelationId.Should().Be(requestCorrelationId);

            var logsResponse = await _client.GetAsync($"/api/logs/correlation/{requestCorrelationId}");
            logsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            _client.DefaultRequestHeaders.Remove("X-Request-Id");
        }
    }

    [Fact]
    public async Task RunCommand_ShouldFallbackCorrelationId_WhenRequestCorrelationHeaderIsTooLong()
    {
        await AuthenticateAsync("ops-correlation-invalid");
        var invalidCorrelationId = new string('a', 256);
        _client.DefaultRequestHeaders.Remove("X-Request-Id");
        _client.DefaultRequestHeaders.TryAddWithoutValidation("X-Request-Id", invalidCorrelationId);

        try
        {
            var response = await _client.PostAsJsonAsync(
                "/api/ops/cli/run",
                new RunCommandDto("health.check"));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var payload = await response.Content.ReadFromJsonAsync<CommandRunDto>();
            payload.Should().NotBeNull();
            payload!.CorrelationId.Should().NotBe(invalidCorrelationId);
            payload.CorrelationId.Length.Should().BeLessOrEqualTo(100);

            response.Headers.TryGetValues("X-Request-Id", out var responseRequestIds).Should().BeTrue();
            responseRequestIds!.Single().Should().Be(payload.CorrelationId);

            var logsResponse = await _client.GetAsync($"/api/logs/correlation/{payload.CorrelationId}");
            logsResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            // Additional check: correlation IDs in the 101–128 range are also constrained to the DB limit (100).
            var midRangeInvalidCorrelationId = new string('b', 101);
            _client.DefaultRequestHeaders.Remove("X-Request-Id");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("X-Request-Id", midRangeInvalidCorrelationId);

            var midRangeResponse = await _client.PostAsJsonAsync(
                "/api/ops/cli/run",
                new RunCommandDto("health.check"));

            midRangeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var midRangePayload = await midRangeResponse.Content.ReadFromJsonAsync<CommandRunDto>();
            midRangePayload.Should().NotBeNull();
            midRangePayload!.CorrelationId.Should().NotBe(midRangeInvalidCorrelationId);
            midRangePayload.CorrelationId.Length.Should().BeLessOrEqualTo(100);

            midRangeResponse.Headers.TryGetValues("X-Request-Id", out var midRangeResponseRequestIds).Should().BeTrue();
            midRangeResponseRequestIds!.Single().Should().Be(midRangePayload.CorrelationId);

            var midRangeLogsResponse = await _client.GetAsync($"/api/logs/correlation/{midRangePayload.CorrelationId}");
            midRangeLogsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            _client.DefaultRequestHeaders.Remove("X-Request-Id");
        }
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
