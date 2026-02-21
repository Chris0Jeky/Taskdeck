using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests;

public class ApiErrorContractApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ApiErrorContractApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ProtectedEndpoint_ShouldReturnUnauthorizedErrorContract_WhenNoToken()
    {
        using var anonymousClient = _factory.CreateClient();

        var response = await anonymousClient.GetAsync("/api/boards");

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.Unauthorized, "Unauthorized");
        response.Headers.WwwAuthenticate.Should().NotBeEmpty();
        response.Headers.WwwAuthenticate
            .Should()
            .Contain(header => string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Login_ShouldReturnAuthenticationFailedErrorContract_WhenCredentialsAreInvalid()
    {
        using var client = _factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var username = $"login_error_{suffix}";
        var email = $"login_error_{suffix}@example.com";
        const string password = "password123";

        var registerResponse = await client.PostAsJsonAsync(
            "/api/auth/register",
            new CreateUserDto(username, email, password));

        registerResponse.EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginDto(username, "wrong-password"));

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.Unauthorized, "AuthenticationFailed");
    }

    [Fact]
    public async Task RegisterConflict_ShouldNotBlockSubsequentLogin_WithExistingCredentials()
    {
        using var client = _factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var username = $"register_conflict_{suffix}";
        var email = $"register_conflict_{suffix}@example.com";
        const string password = "password123";

        var initialRegisterResponse = await client.PostAsJsonAsync(
            "/api/auth/register",
            new CreateUserDto(username, email, password));
        initialRegisterResponse.EnsureSuccessStatusCode();

        var duplicateRegisterResponse = await client.PostAsJsonAsync(
            "/api/auth/register",
            new CreateUserDto(username, email, "different-password"));
        await ApiTestHarness.AssertErrorContractAsync(duplicateRegisterResponse, HttpStatusCode.Conflict, "Conflict");

        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginDto(username, password));
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginPayload = await loginResponse.Content.ReadFromJsonAsync<AuthResultDto>();
        loginPayload.Should().NotBeNull();
        loginPayload!.Token.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Login_ShouldDifferentiateInvalidCredentials_FromInactiveAccountState()
    {
        using var client = _factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var username = $"inactive_login_{suffix}";
        var email = $"inactive_login_{suffix}@example.com";
        const string password = "password123";

        var registerResponse = await client.PostAsJsonAsync(
            "/api/auth/register",
            new CreateUserDto(username, email, password));
        registerResponse.EnsureSuccessStatusCode();
        var registerPayload = await registerResponse.Content.ReadFromJsonAsync<AuthResultDto>();
        registerPayload.Should().NotBeNull();

        var invalidCredentialsResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginDto(username, "wrong-password"));
        await ApiTestHarness.AssertErrorContractAsync(invalidCredentialsResponse, HttpStatusCode.Unauthorized, "AuthenticationFailed");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registerPayload!.Token);
        var deactivateResponse = await client.PostAsync($"/api/users/{registerPayload.User.Id}/deactivate", content: null);
        deactivateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        client.DefaultRequestHeaders.Authorization = null;

        var inactiveLoginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginDto(username, password));
        await ApiTestHarness.AssertErrorContractAsync(inactiveLoginResponse, HttpStatusCode.Forbidden, "Forbidden");
    }

    [Fact]
    public async Task CreateBoard_ShouldReturnValidationErrorContract_WhenNameIsEmpty()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "error-contract");

        var response = await client.PostAsJsonAsync(
            "/api/boards",
            new CreateBoardDto(string.Empty, "invalid"));

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.BadRequest, "ValidationError");
    }
}
