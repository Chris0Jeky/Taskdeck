using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests;

public class ActiveUserValidationIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ActiveUserValidationIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ActiveUser_CanAccessProtectedEndpoints()
    {
        var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "active-user");

        var response = await client.GetAsync("/api/boards");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeletedUser_Gets401_OnSubsequentAuthenticatedRequests()
    {
        var client = _factory.CreateClient();
        var userCtx = await ApiTestHarness.AuthenticateAsync(client, "deletion-test");

        // Verify the user can access protected endpoints before deletion
        var beforeResponse = await client.GetAsync("/api/boards");
        beforeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Delete the account
        var deleteResponse = await client.PostAsJsonAsync("/api/account/delete", new
        {
            currentPassword = "password123",
            confirmationPhrase = "DELETE MY ACCOUNT"
        });
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // The same JWT should now be rejected with 401
        var afterResponse = await client.GetAsync("/api/boards");
        afterResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await ApiTestHarness.AssertErrorContractAsync(afterResponse, HttpStatusCode.Unauthorized, "Unauthorized");
    }

    [Fact]
    public async Task DeletedUser_Gets401_Immediately_WithoutCacheDelay()
    {
        var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "immediate-invalidation");

        // Access a protected endpoint to warm the cache
        var warmupResponse = await client.GetAsync("/api/boards");
        warmupResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Delete the account (this should invalidate the cache)
        var deleteResponse = await client.PostAsJsonAsync("/api/account/delete", new
        {
            currentPassword = "password123",
            confirmationPhrase = "DELETE MY ACCOUNT"
        });
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Immediately try again — should be rejected (cache was invalidated, not just expired)
        var afterResponse = await client.GetAsync("/api/boards");
        afterResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UnauthenticatedRequests_AreNotAffectedByMiddleware()
    {
        var client = _factory.CreateClient();

        // Anonymous endpoints should work normally
        var healthResponse = await client.GetAsync("/health/live");
        healthResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OtherUsers_AreNotAffectedByDeletion()
    {
        var client1 = _factory.CreateClient();
        var client2 = _factory.CreateClient();

        await ApiTestHarness.AuthenticateAsync(client1, "user-to-delete");
        await ApiTestHarness.AuthenticateAsync(client2, "other-user");

        // Delete user 1's account
        var deleteResponse = await client1.PostAsJsonAsync("/api/account/delete", new
        {
            currentPassword = "password123",
            confirmationPhrase = "DELETE MY ACCOUNT"
        });
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // User 1 should be rejected
        var user1Response = await client1.GetAsync("/api/boards");
        user1Response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // User 2 should still work fine
        var user2Response = await client2.GetAsync("/api/boards");
        user2Response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
