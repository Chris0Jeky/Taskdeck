using System.Net;
using System.Net.Http;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Guards the global authorization FallbackPolicy (#1132 AC4): endpoints without explicit
/// authorization metadata require an authenticated user, while the SPA shell, the API-key-gated
/// /mcp endpoint, and the [AllowAnonymous] auth/health endpoints stay reachable.
/// </summary>
public class FallbackPolicyTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public FallbackPolicyTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task FallbackPolicy_RequiringAuthenticatedUser_IsRegistered()
    {
        // Directly proves SetFallbackPolicy(RequireAuthenticatedUser) was applied — this assertion
        // (unlike the endpoint tests, which exercise explicit [Authorize]/[AllowAnonymous] metadata)
        // fails if SetFallbackPolicy is removed, since GetFallbackPolicyAsync then returns null.
        using var scope = _factory.Services.CreateScope();
        var policyProvider = scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>();

        var fallback = await policyProvider.GetFallbackPolicyAsync();

        fallback.Should().NotBeNull("the global FallbackPolicy must be configured (#1132 AC4)");
        fallback!.Requirements.Should().Contain(r => r is DenyAnonymousAuthorizationRequirement);
    }

    [Fact]
    public async Task AnonymousAllowlistedEndpoint_StillReachable_UnderFallbackPolicy()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ProtectedEndpoint_Returns401_WhenAnonymous()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/boards");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SpaFallback_NotBlockedByFallbackPolicy_WhenAnonymous()
    {
        using var client = _factory.CreateClient();

        // A non-API/non-hub route hits MapFallbackToFile, which is AllowAnonymous: the fallback
        // policy must NOT convert this into a 401 (it serves the shell, or 404 if the test host
        // ships no wwwroot). Without the AllowAnonymous opt-out this would be 401.
        var response = await client.GetAsync("/some/client/route");

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task McpEndpoint_WithoutApiKey_Returns401_FromApiKeyMiddleware()
    {
        using var client = _factory.CreateClient();

        // /mcp is AllowAnonymous to the JWT policy but gated by ApiKeyMiddleware, which rejects a
        // missing key with 401 before routing — proving the fallback opt-out did not bypass MCP auth.
        using var content = new StringContent(string.Empty);
        var response = await client.PostAsync("/mcp", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
