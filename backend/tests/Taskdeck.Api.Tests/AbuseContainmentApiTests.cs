using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Api.Tests;

public class AbuseContainmentApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public AbuseContainmentApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetActorStatus_ShouldReturnUnauthorized_WhenNoToken()
    {
        using var client = _factory.CreateClient();
        var actorId = Guid.NewGuid();

        var response = await client.GetAsync($"/api/abuse/actors/{actorId}/status");

        await ApiTestHarness.AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task GetAuditTrail_ShouldReturnUnauthorized_WhenNoToken()
    {
        using var client = _factory.CreateClient();
        var actorId = Guid.NewGuid();

        var response = await client.GetAsync($"/api/abuse/actors/{actorId}/events");

        await ApiTestHarness.AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task Override_ShouldReturnUnauthorized_WhenNoToken()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/abuse/actors/override",
            new AbuseOverrideRequestDto(Guid.NewGuid(), AbuseState.Restricted, "test override"));

        await ApiTestHarness.AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task EvaluateActor_ShouldReturnUnauthorized_WhenNoToken()
    {
        using var client = _factory.CreateClient();
        var actorId = Guid.NewGuid();

        var response = await client.PostAsync($"/api/abuse/actors/{actorId}/evaluate", null);

        await ApiTestHarness.AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task GetActorStatus_ShouldReturnOk_ForAuthenticatedUser()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "abuse-status");

        var response = await client.GetAsync($"/api/abuse/actors/{user.UserId}/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotBeNullOrWhiteSpace();

        // Verify it's valid JSON with expected shape
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("userId", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("currentState", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetAuditTrail_ShouldReturnOk_ForAuthenticatedUser()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "abuse-audit");

        var response = await client.GetAsync($"/api/abuse/actors/{user.UserId}/events");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        // Should be a JSON array (possibly empty for a new user)
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetAuditTrail_InvalidLimit_ShouldReturn400()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "abuse-audit-invalid");

        var response = await client.GetAsync($"/api/abuse/actors/{user.UserId}/events?limit=0");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.BadRequest, "ValidationError");
    }

    [Fact]
    public async Task GetAuditTrail_LimitTooHigh_ShouldReturn400()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "abuse-audit-high");

        var response = await client.GetAsync($"/api/abuse/actors/{user.UserId}/events?limit=999");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.BadRequest, "ValidationError");
    }

    [Fact]
    public async Task Override_WithEmptyActorId_ShouldReturn400()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "abuse-override-empty");

        var response = await client.PostAsJsonAsync("/api/abuse/actors/override",
            new AbuseOverrideRequestDto(Guid.Empty, AbuseState.Restricted, "test reason"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.BadRequest, "ValidationError");
    }

    [Fact]
    public async Task Override_WithEmptyReason_ShouldReturn400()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "abuse-override-noreason");

        var response = await client.PostAsJsonAsync("/api/abuse/actors/override",
            new AbuseOverrideRequestDto(user.UserId, AbuseState.Restricted, ""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.BadRequest, "ValidationError");
    }

    [Fact]
    public async Task Override_ValidRequest_ShouldReturnOk()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "abuse-override-valid");

        var response = await client.PostAsJsonAsync("/api/abuse/actors/override",
            new AbuseOverrideRequestDto(user.UserId, AbuseState.Restricted, "operator test override"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("currentState", out _).Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateActor_ShouldReturnOk_ForAuthenticatedUser()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "abuse-evaluate");

        var response = await client.PostAsync($"/api/abuse/actors/{user.UserId}/evaluate", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("signalsDetected", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("status", out _).Should().BeTrue();
    }

    // NOTE: Cross-user isolation tests for abuse endpoints are intentionally omitted.
    // The AbuseContainmentController currently allows any authenticated user to query
    // any actorUserId status/events and override any actor state. This is a known
    // design gap (missing operator-only RBAC) flagged by Gemini review. It should be
    // addressed as a security fix, not papered over by tests that document broken behavior.
}
