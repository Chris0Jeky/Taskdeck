using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests.Resilience;

/// <summary>
/// Tests that external service failures (GitHub OAuth, etc.) produce appropriate
/// error responses while keeping local functionality working.
/// </summary>
public class ExternalServiceFailureTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ExternalServiceFailureTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    // ── Local Auth Still Works When External Auth Is Unavailable ───────

    [Fact]
    public async Task LocalRegistration_ShouldWork_RegardlessOfExternalOAuthState()
    {
        // Local auth (register + login) should not depend on any external service.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var response = await _client.PostAsJsonAsync(
            "/api/auth/register",
            new CreateUserDto($"ext-resilience-{suffix}", $"ext-resilience-{suffix}@example.com", "password123"));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "local registration should succeed regardless of external service state");

        var payload = await response.Content.ReadFromJsonAsync<AuthResultDto>();
        payload.Should().NotBeNull();
        payload!.Token.Should().NotBeNullOrWhiteSpace(
            "local auth should issue a token without relying on external services");
    }

    [Fact]
    public async Task LocalLogin_ShouldWork_RegardlessOfExternalOAuthState()
    {
        // Register first.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var username = $"ext-login-{suffix}";
        var registerResponse = await _client.PostAsJsonAsync(
            "/api/auth/register",
            new CreateUserDto(username, $"ext-login-{suffix}@example.com", "password123"));
        registerResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Login should work via local path regardless of external service availability.
        var loginResponse = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginDto(username, "password123"));

        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "local login should succeed regardless of external service state");

        var loginPayload = await loginResponse.Content.ReadFromJsonAsync<AuthResultDto>();
        loginPayload.Should().NotBeNull();
        loginPayload!.Token.Should().NotBeNullOrWhiteSpace();
    }

    // ── Invalid External Auth Callback → Appropriate Error ────────────

    [Fact]
    public async Task GithubCallback_WhenGithubNotConfigured_ReturnsNotFound()
    {
        // When GitHub OAuth is not configured, the callback should return
        // a clean 404 error rather than crashing.
        var response = await _client.GetAsync("/api/auth/github/callback");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "GitHub callback should return 404 when OAuth is not configured");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("errorCode", out var errorCode).Should().BeTrue(
            "404 response should follow the error contract");
        errorCode.GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task GithubLogin_WhenGithubNotConfigured_ReturnsNotFound()
    {
        // The GitHub login initiation endpoint should also return 404 when not configured.
        var response = await _client.GetAsync("/api/auth/github/login");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "GitHub login should return 404 when OAuth is not configured");
    }

    // ── API Endpoints Return Proper Error Codes on Invalid Input ──────

    [Fact]
    public async Task ApiEndpoints_ReturnProperErrorCodes_WhenUnauthenticated()
    {
        // Without auth, protected endpoints should return 401, not 500.
        var boardsResponse = await _client.GetAsync("/api/boards");
        boardsResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "unauthenticated request to boards should get 401, not 500");

        var captureResponse = await _client.GetAsync("/api/capture/items");
        captureResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "unauthenticated request to capture should get 401, not 500");

        var chatResponse = await _client.GetAsync("/api/llm/chat/sessions");
        chatResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "unauthenticated request to chat sessions should get 401, not 500");
    }
}
