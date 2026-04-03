using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests;

public class UsersApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private bool _isAuthenticated;
    private TestUserContext? _currentUser;

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
            await _client.PutAsJsonAsync(
                $"/api/users/{userId}",
                new UpdateUserDto($"updated_{Guid.NewGuid():N}", $"updated_{Guid.NewGuid():N}@example.com")));

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
    public async Task GetUser_ShouldReturnCurrentUser_WhenRequestingSelf()
    {
        var currentUser = await EnsureAuthenticatedAsync();

        var response = await _client.GetAsync($"/api/users/{currentUser.UserId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var user = await response.Content.ReadFromJsonAsync<UserDto>();
        user.Should().NotBeNull();
        user!.Id.Should().Be(currentUser.UserId);
    }

    [Fact]
    public async Task GetUser_ShouldReturnForbidden_WhenRequestingAnotherUser()
    {
        await EnsureAuthenticatedAsync();
        var (_, _, _, otherUserId) = await RegisterUserAsync("other_get");

        var response = await _client.GetAsync($"/api/users/{otherUserId}");

        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    [Fact]
    public async Task GetUserByUsername_ShouldReturnCurrentUser_WhenRequestingSelfUsername()
    {
        var currentUser = await EnsureAuthenticatedAsync();

        var response = await _client.GetAsync($"/api/users/by-username/{currentUser.Username}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var user = await response.Content.ReadFromJsonAsync<UserDto>();
        user.Should().NotBeNull();
        user!.Id.Should().Be(currentUser.UserId);
    }

    [Fact]
    public async Task GetUserByUsername_ShouldReturnForbidden_WhenRequestingDifferentUsername()
    {
        await EnsureAuthenticatedAsync();
        var (username, _, _, _) = await RegisterUserAsync("other_by_name");

        var response = await _client.GetAsync($"/api/users/by-username/{username}");

        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    [Fact]
    public async Task GetUserByUsername_ShouldReturnForbidden_WhenRequestingSelfUsernameWithDifferentCase()
    {
        var currentUser = await EnsureAuthenticatedAsync();
        var differentCaseUsername = currentUser.Username.ToUpperInvariant();

        if (differentCaseUsername == currentUser.Username)
        {
            differentCaseUsername = currentUser.Username.ToLowerInvariant();
        }

        var response = await _client.GetAsync($"/api/users/by-username/{differentCaseUsername}");

        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    [Fact]
    public async Task UpdateUser_ShouldReturnOk_WhenUpdatingSelf()
    {
        var currentUser = await EnsureAuthenticatedAsync();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var updateDto = new UpdateUserDto($"updated_{suffix}", $"updated_{suffix}@example.com");

        var response = await _client.PutAsJsonAsync($"/api/users/{currentUser.UserId}", updateDto);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var user = await response.Content.ReadFromJsonAsync<UserDto>();
        user.Should().NotBeNull();
        user!.Username.Should().Be(updateDto.Username);
    }

    [Fact]
    public async Task UpdateUser_ShouldReturnForbidden_WhenUpdatingAnotherUser()
    {
        await EnsureAuthenticatedAsync();
        var (_, _, _, otherUserId) = await RegisterUserAsync("other_update");

        var response = await _client.PutAsJsonAsync(
            $"/api/users/{otherUserId}",
            new UpdateUserDto($"updated_{Guid.NewGuid():N}", $"updated_{Guid.NewGuid():N}@example.com"));

        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    [Fact]
    public async Task DeactivateUser_ShouldReturnNoContent_WhenDeactivatingSelf()
    {
        var currentUser = await EnsureAuthenticatedAsync();

        var response = await _client.PostAsync($"/api/users/{currentUser.UserId}/deactivate", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ActivateUser_ShouldReturn401_AfterSelfDeactivation()
    {
        // After deactivation, the ActiveUserValidationMiddleware rejects the user's JWT,
        // preventing self-reactivation. An admin (or fresh login) is required to reactivate.
        var currentUser = await EnsureAuthenticatedAsync();

        var deactivateResponse = await _client.PostAsync($"/api/users/{currentUser.UserId}/deactivate", null);
        deactivateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var activateResponse = await _client.PostAsync($"/api/users/{currentUser.UserId}/activate", null);
        activateResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeactivateUser_ShouldReturnForbidden_WhenDeactivatingAnotherUser()
    {
        await EnsureAuthenticatedAsync();
        var (_, _, _, otherUserId) = await RegisterUserAsync("other_deactivate");

        var response = await _client.PostAsync($"/api/users/{otherUserId}/deactivate", null);

        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    [Fact]
    public async Task ActivateUser_ShouldReturnForbidden_WhenActivatingAnotherUser()
    {
        await EnsureAuthenticatedAsync();
        var (_, _, _, otherUserId) = await RegisterUserAsync("other_activate");

        var response = await _client.PostAsync($"/api/users/{otherUserId}/activate", null);

        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    [Fact]
    public async Task GetUsers_ShouldReturnOnlyCurrentUser()
    {
        var currentUser = await EnsureAuthenticatedAsync();
        await RegisterUserAsync("list_other");

        var response = await _client.GetAsync("/api/users");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var users = await response.Content.ReadFromJsonAsync<List<UserDto>>();
        users.Should().NotBeNull();
        users.Should().HaveCount(1);
        users![0].Id.Should().Be(currentUser.UserId);
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

    private async Task<TestUserContext> EnsureAuthenticatedAsync()
    {
        if (_isAuthenticated && _currentUser is not null)
        {
            return _currentUser;
        }

        _currentUser = await ApiTestHarness.AuthenticateAsync(_client, "users-suite");
        _isAuthenticated = true;
        return _currentUser;
    }
}
