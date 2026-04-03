using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Integration tests for the ChangePassword endpoint (SEC-20 / #722).
/// Verifies that password changes are only allowed for the authenticated caller's own account,
/// preventing cross-user password mutation.
/// </summary>
public class ChangePasswordApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ChangePasswordApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ChangePassword_Unauthenticated_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            CurrentPassword = "password123",
            NewPassword = "NewPassword!456"
        });

        await ApiTestHarness.AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task ChangePassword_OwnAccount_Succeeds()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "chpwd-own");

        var response = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            CurrentPassword = "password123",
            NewPassword = "NewPassword!456"
        });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify the new password works by logging in again
        using var loginClient = _factory.CreateClient();
        var loginResponse = await loginClient.PostAsJsonAsync("/api/auth/login", new
        {
            UsernameOrEmail = user.Username,
            Password = "NewPassword!456"
        });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_Returns401()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "chpwd-wrong");

        var response = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            CurrentPassword = "wrong-password",
            NewPassword = "NewPassword!456"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ChangePassword_NoUserIdInBody_DeriveFromJwt()
    {
        // Even if a malicious client sends a UserId field in the body, the endpoint
        // should ignore it and derive the target user from JWT claims.
        using var clientA = _factory.CreateClient();
        var userA = await ApiTestHarness.AuthenticateAsync(clientA, "chpwd-ignore-body");

        using var clientB = _factory.CreateClient();
        var userB = await ApiTestHarness.AuthenticateAsync(clientB, "chpwd-victim");

        // User A attempts to change password with User B's GUID in body — should be ignored
        var response = await clientA.PostAsJsonAsync("/api/auth/change-password", new
        {
            UserId = userB.UserId,
            CurrentPassword = "password123",
            NewPassword = "Hacked!456"
        });

        // Should succeed — but only for User A's own account (UserId in body is ignored)
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify User B's password was NOT changed — they can still log in with original password
        using var verifyClient = _factory.CreateClient();
        var loginB = await verifyClient.PostAsJsonAsync("/api/auth/login", new
        {
            UsernameOrEmail = userB.Username,
            Password = "password123"
        });
        loginB.StatusCode.Should().Be(HttpStatusCode.OK, "User B's password should not have been changed");

        // Verify User A's password WAS changed
        var loginA = await verifyClient.PostAsJsonAsync("/api/auth/login", new
        {
            UsernameOrEmail = userA.Username,
            Password = "Hacked!456"
        });
        loginA.StatusCode.Should().Be(HttpStatusCode.OK, "User A's password should have been changed");
    }

    [Fact]
    public async Task ChangePassword_ExpiredOrInvalidToken_Returns401()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");

        var response = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            CurrentPassword = "password123",
            NewPassword = "NewPassword!456"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
