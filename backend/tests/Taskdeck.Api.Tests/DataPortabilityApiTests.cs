using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests;

public class DataPortabilityApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public DataPortabilityApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ExportUserData_ShouldReturnUnauthorized_WhenNoToken()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/account/export");

        await ApiTestHarness.AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task ExportUserData_ShouldReturnVersionedExport_ForAuthenticatedUser()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "export-user");

        // Create a board so export has some content
        await ApiTestHarness.CreateBoardAsync(client, "export-board");

        var response = await client.GetAsync("/api/account/export");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotBeNullOrWhiteSpace();

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("userId", out var userIdProp).Should().BeTrue();
        doc.RootElement.TryGetProperty("version", out var versionProp).Should().BeTrue();
        versionProp.GetString().Should().NotBeNullOrWhiteSpace();
        doc.RootElement.TryGetProperty("profile", out var profileProp).Should().BeTrue();
        profileProp.TryGetProperty("username", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("data", out var dataProp).Should().BeTrue();
        dataProp.ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task ExportUserData_ShouldHaveNoCacheHeader()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "export-cache");

        var response = await client.GetAsync("/api/account/export");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.NoStore.Should().BeTrue(
            "data export should not be cached due to ResponseCache(NoStore=true)");
    }

    [Fact]
    public async Task DeleteAccount_ShouldReturnUnauthorized_WhenNoToken()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/account/delete",
            new AccountDeletionRequest("password123", "DELETE MY ACCOUNT"));

        await ApiTestHarness.AssertUnauthorizedAsync(response);
    }

    [Fact]
    public async Task DeleteAccount_WithWrongPassword_ShouldReturn401()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "delete-wrongpw");

        var response = await client.PostAsJsonAsync("/api/account/delete",
            new AccountDeletionRequest("wrong-password", "DELETE MY ACCOUNT"));

        // Wrong password maps to AuthenticationFailed -> 401
        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.Unauthorized, "AuthenticationFailed");
    }

    [Fact]
    public async Task DeleteAccount_WithWrongConfirmation_ShouldReturn400()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "delete-wrongconfirm");

        var response = await client.PostAsJsonAsync("/api/account/delete",
            new AccountDeletionRequest("password123", "wrong confirmation"));

        // Wrong confirmation phrase maps to ValidationError -> 400
        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.BadRequest, "ValidationError");
    }

    [Fact]
    public async Task DeleteAccount_WithCorrectCredentials_ShouldSucceed()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "delete-success");

        var response = await client.PostAsJsonAsync("/api/account/delete",
            new AccountDeletionRequest("password123", "DELETE MY ACCOUNT"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<AccountDeletionResultDto>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAccount_ThenLogin_ShouldFail()
    {
        // Register and then delete
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "delete-then-login");

        var deleteResponse = await client.PostAsJsonAsync("/api/account/delete",
            new AccountDeletionRequest("password123", "DELETE MY ACCOUNT"));
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Attempt to login with the deleted user's credentials
        using var loginClient = _factory.CreateClient();
        var loginResponse = await loginClient.PostAsJsonAsync("/api/auth/login",
            new LoginDto(user.Email, "password123"));

        // Deleted user password hash and email are randomized, so login must fail with 401
        await ApiTestHarness.AssertErrorContractAsync(loginResponse, HttpStatusCode.Unauthorized, "AuthenticationFailed");
    }

    [Fact]
    public async Task ExportUserData_CrossUserIsolation_ShouldScopeToRequestingUser()
    {
        // User A exports
        using var clientA = _factory.CreateClient();
        var userA = await ApiTestHarness.AuthenticateAsync(clientA, "export-isolation-a");

        // User B exports
        using var clientB = _factory.CreateClient();
        var userB = await ApiTestHarness.AuthenticateAsync(clientB, "export-isolation-b");

        // Each user's export should be scoped to their own userId
        var exportA = await clientA.GetAsync("/api/account/export");
        exportA.StatusCode.Should().Be(HttpStatusCode.OK);
        var bodyA = await exportA.Content.ReadAsStringAsync();
        using var docA = JsonDocument.Parse(bodyA);
        docA.RootElement.GetProperty("userId").GetString().Should().Be(userA.UserId.ToString());

        var exportB = await clientB.GetAsync("/api/account/export");
        exportB.StatusCode.Should().Be(HttpStatusCode.OK);
        var bodyB = await exportB.Content.ReadAsStringAsync();
        using var docB = JsonDocument.Parse(bodyB);
        docB.RootElement.GetProperty("userId").GetString().Should().Be(userB.UserId.ToString());

        // Verify they're different users
        userA.UserId.Should().NotBe(userB.UserId);
    }
}
