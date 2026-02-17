using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests;

public class UsersApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private bool _isAuthenticated;

    public UsersApiTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task UsersEndpoints_ShouldReturnUnauthorized_WhenNoToken()
    {
        var userId = Guid.NewGuid();

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.GetAsync("/api/users"));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.GetAsync($"/api/users/{userId}"));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.GetAsync("/api/users/by-username/unauthorized"));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.PostAsJsonAsync(
                "/api/users",
                new CreateUserDto($"user_{Guid.NewGuid():N}", $"user_{Guid.NewGuid():N}@example.com", "password123")));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.PostAsync($"/api/users/{userId}/deactivate", null));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.PostAsync($"/api/users/{userId}/activate", null));
    }

    [Fact]
    public async Task CreateUser_ShouldReturnCreated_WithValidData()
    {
        await EnsureAuthenticatedAsync();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var dto = new CreateUserDto($"user_{suffix}", $"user_{suffix}@example.com", "password123");

        var response = await _client.PostAsJsonAsync("/api/users", dto);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var user = await response.Content.ReadFromJsonAsync<UserDto>();
        user.Should().NotBeNull();
        user!.Username.Should().Be(dto.Username);
        user.Email.Should().Be(dto.Email);
        user.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetUser_ShouldReturnNotFound_WhenUserDoesNotExist()
    {
        await EnsureAuthenticatedAsync();

        var response = await _client.GetAsync($"/api/users/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task GetUserByUsername_ShouldReturnUser_WhenUserExists()
    {
        var (username, _, _, _) = await RegisterUserAsync("byname");
        await EnsureAuthenticatedAsync();

        var response = await _client.GetAsync($"/api/users/by-username/{username}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var user = await response.Content.ReadFromJsonAsync<UserDto>();
        user.Should().NotBeNull();
        user!.Username.Should().Be(username);
    }

    [Fact]
    public async Task GetUserByUsername_ShouldReturnNotFound_WhenUsernameDoesNotExist()
    {
        await EnsureAuthenticatedAsync();

        var response = await _client.GetAsync("/api/users/by-username/nonexistent_user_xyz");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task DeactivateUser_ShouldReturnNoContent()
    {
        var (_, _, _, userId) = await RegisterUserAsync("deact");
        await EnsureAuthenticatedAsync();

        var response = await _client.PostAsync($"/api/users/{userId}/deactivate", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ActivateUser_ShouldReturnNoContent_AfterDeactivation()
    {
        var (_, _, _, userId) = await RegisterUserAsync("activ");
        await EnsureAuthenticatedAsync();

        var deactivateResponse = await _client.PostAsync($"/api/users/{userId}/deactivate", null);
        deactivateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var activateResponse = await _client.PostAsync($"/api/users/{userId}/activate", null);
        activateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await _client.GetAsync($"/api/users/{userId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var user = await getResponse.Content.ReadFromJsonAsync<UserDto>();
        user.Should().NotBeNull();
        user!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetUsers_ShouldReturnListOfUsers()
    {
        await RegisterUserAsync("listuser");
        await EnsureAuthenticatedAsync();

        var response = await _client.GetAsync("/api/users");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var users = await response.Content.ReadFromJsonAsync<List<UserDto>>();
        users.Should().NotBeNull();
        users!.Count.Should().BeGreaterThanOrEqualTo(1);
    }

    private async Task<(string Username, string Email, string Password, Guid UserId)> RegisterUserAsync(string stem)
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

        return (username, email, password, payload!.User.Id);
    }

    private async Task EnsureAuthenticatedAsync()
    {
        if (_isAuthenticated)
        {
            return;
        }

        await ApiTestHarness.AuthenticateAsync(_client, "users-suite");
        _isAuthenticated = true;
    }
}
