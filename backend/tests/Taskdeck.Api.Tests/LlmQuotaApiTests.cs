using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Api.Tests;

public class LlmQuotaApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public LlmQuotaApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetQuotaUsage_ShouldRequireAuth()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/llm/quota/usage");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetQuotaStatus_ShouldRequireAuth()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/llm/quota/status");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetKillSwitch_ShouldRequireAuth()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/llm/killswitch");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostKillSwitch_ShouldRequireAuth()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/llm/killswitch", new
        {
            scope = (int)KillSwitchScope.Global,
            target = (string?)null,
            enabled = true,
            reason = "test"
        });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetQuotaStatus_ShouldReturnCorrectShape()
    {
        using var client = _factory.CreateClient();
        await AuthenticateAsync(client, "quota-status");

        var response = await client.GetAsync("/api/llm/quota/status");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, because: body);

        var json = JsonSerializer.Deserialize<JsonElement>(body);
        json.TryGetProperty("allowed", out _).Should().BeTrue();
        json.TryGetProperty("tokensUsedToday", out _).Should().BeTrue();
        json.TryGetProperty("tokenBudgetCeiling", out _).Should().BeTrue();
        json.TryGetProperty("requestsThisHour", out _).Should().BeTrue();
        json.TryGetProperty("requestsPerHourLimit", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetQuotaUsage_ShouldReturnUsageSummary()
    {
        using var client = _factory.CreateClient();
        await AuthenticateAsync(client, "quota-usage");

        var response = await client.GetAsync("/api/llm/quota/usage");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, because: body);

        var json = JsonSerializer.Deserialize<JsonElement>(body);
        json.TryGetProperty("totalRequests", out _).Should().BeTrue();
        json.TryGetProperty("totalTokens", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetKillSwitch_ShouldReturnStatus()
    {
        using var client = _factory.CreateClient();
        await AuthenticateAsync(client, "killswitch-get");

        var response = await client.GetAsync("/api/llm/killswitch");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, because: body);

        var json = JsonSerializer.Deserialize<JsonElement>(body);
        json.TryGetProperty("globalKilled", out _).Should().BeTrue();
        json.TryGetProperty("entries", out var entries).Should().BeTrue();
        entries.GetArrayLength().Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public async Task PostKillSwitch_GlobalScope_ShouldReturn403()
    {
        using var client = _factory.CreateClient();
        await AuthenticateAsync(client, "killswitch-global");

        var response = await client.PostAsJsonAsync("/api/llm/killswitch", new
        {
            scope = (int)KillSwitchScope.Global,
            target = (string?)null,
            enabled = true,
            reason = "test"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostKillSwitch_SurfaceScope_ShouldReturn403()
    {
        using var client = _factory.CreateClient();
        await AuthenticateAsync(client, "killswitch-surface");

        var response = await client.PostAsJsonAsync("/api/llm/killswitch", new
        {
            scope = (int)KillSwitchScope.Surface,
            target = "Worker",
            enabled = true,
            reason = "integration test"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostKillSwitch_IdentityScope_OwnUser_ShouldSucceed()
    {
        using var client = _factory.CreateClient();
        var userId = await AuthenticateAndGetUserIdAsync(client, "killswitch-own");

        var response = await client.PostAsJsonAsync("/api/llm/killswitch", new
        {
            scope = (int)KillSwitchScope.Identity,
            target = userId.ToString(),
            enabled = true,
            reason = "self-kill"
        });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, because: body);
    }

    [Fact]
    public async Task PostKillSwitch_IdentityScope_OtherUser_ShouldReturn403()
    {
        using var client = _factory.CreateClient();
        await AuthenticateAsync(client, "killswitch-other");

        var response = await client.PostAsJsonAsync("/api/llm/killswitch", new
        {
            scope = (int)KillSwitchScope.Identity,
            target = Guid.NewGuid().ToString(),
            enabled = true,
            reason = "trying to kill another user"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task AuthenticateAsync(HttpClient client, string stem)
    {
        await AuthenticateAndGetUserIdAsync(client, stem);
    }

    private static async Task<Guid> AuthenticateAndGetUserIdAsync(HttpClient client, string stem)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var username = $"{stem}_{suffix}";
        var email = $"{stem}_{suffix}@example.com";

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { username, email, password = "password123" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = payload.GetProperty("token").GetString();
        var userId = payload.GetProperty("user").GetProperty("id").GetGuid();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return userId;
    }
}
