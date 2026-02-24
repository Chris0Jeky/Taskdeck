using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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

    private static string CreateRequestId(string stem) => $"{stem}-{Guid.NewGuid():N}";

    private static void AssertRequestIdEcho(HttpResponseMessage response, string requestId)
    {
        response.Headers.TryGetValues("X-Request-Id", out var requestIdValues).Should().BeTrue();
        requestIdValues.Should().ContainSingle().Which.Should().Be(requestId);
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
    public async Task ProtectedEndpoint_ShouldEchoRequestId_ForUnauthorizedErrorContract()
    {
        using var anonymousClient = _factory.CreateClient();
        var requestId = CreateRequestId("unauthorized-contract");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/boards");
        request.Headers.Add("X-Request-Id", requestId);

        var response = await anonymousClient.SendAsync(request);

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.Unauthorized, "Unauthorized");
        AssertRequestIdEcho(response, requestId);
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
    public async Task Register_ShouldEchoRequestId_ForConflictErrorContract()
    {
        using var client = _factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var username = $"register_conflict_header_{suffix}";
        var email = $"register_conflict_header_{suffix}@example.com";
        const string password = "password123";

        var initialRegisterResponse = await client.PostAsJsonAsync(
            "/api/auth/register",
            new CreateUserDto(username, email, password));
        initialRegisterResponse.EnsureSuccessStatusCode();

        var requestId = CreateRequestId("conflict-contract");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(new CreateUserDto(username, email, "different-password"))
        };
        request.Headers.Add("X-Request-Id", requestId);

        var duplicateRegisterResponse = await client.SendAsync(request);

        await ApiTestHarness.AssertErrorContractAsync(duplicateRegisterResponse, HttpStatusCode.Conflict, "Conflict");
        AssertRequestIdEcho(duplicateRegisterResponse, requestId);
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
    public async Task BoardRead_ShouldEchoRequestId_ForCrossUserForbiddenErrorContract()
    {
        using var ownerClient = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(ownerClient, "error-contract-owner");
        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, stem: "forbidden-contract");

        using var crossUserClient = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(crossUserClient, "error-contract-other");
        var requestId = CreateRequestId("forbidden-contract");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/boards/{board.Id}");
        request.Headers.Add("X-Request-Id", requestId);

        var response = await crossUserClient.SendAsync(request);

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.Forbidden, "Forbidden");
        AssertRequestIdEcho(response, requestId);
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
    public async Task Login_ShouldUseSameAuthenticationFailedMessage_ForUnknownIdentifierAndWrongPassword()
    {
        using var client = _factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var username = $"auth_enum_{suffix}";
        var email = $"auth_enum_{suffix}@example.com";
        const string password = "password123";

        var registerResponse = await client.PostAsJsonAsync(
            "/api/auth/register",
            new CreateUserDto(username, email, password));
        registerResponse.EnsureSuccessStatusCode();

        var wrongPasswordResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginDto(username, "wrong-password"));
        await ApiTestHarness.AssertErrorContractAsync(wrongPasswordResponse, HttpStatusCode.Unauthorized, "AuthenticationFailed");
        var wrongPasswordPayload = await wrongPasswordResponse.Content.ReadFromJsonAsync<JsonElement>();

        var unknownUserResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginDto($"unknown_{suffix}", "wrong-password"));
        await ApiTestHarness.AssertErrorContractAsync(unknownUserResponse, HttpStatusCode.Unauthorized, "AuthenticationFailed");
        var unknownUserPayload = await unknownUserResponse.Content.ReadFromJsonAsync<JsonElement>();

        wrongPasswordPayload.GetProperty("message").GetString()
            .Should()
            .Be(unknownUserPayload.GetProperty("message").GetString());
    }

    [Fact]
    public async Task BoardRead_ShouldEchoRequestId_ForMissingResourceNotFoundErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "missing-error-contract");
        var missingBoardId = Guid.NewGuid();
        var requestId = CreateRequestId("notfound-contract");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/boards/{missingBoardId}");
        request.Headers.Add("X-Request-Id", requestId);

        var response = await client.SendAsync(request);

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound, "NotFound");
        AssertRequestIdEcho(response, requestId);
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

    [Fact]
    public async Task CreateBoard_ShouldEchoRequestId_ForValidationErrorContract()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "validation-header-contract");
        var requestId = CreateRequestId("validation-contract");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/boards")
        {
            Content = JsonContent.Create(new CreateBoardDto(string.Empty, "invalid"))
        };
        request.Headers.Add("X-Request-Id", requestId);

        var response = await client.SendAsync(request);

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.BadRequest, "ValidationError");
        AssertRequestIdEcho(response, requestId);
    }
}
