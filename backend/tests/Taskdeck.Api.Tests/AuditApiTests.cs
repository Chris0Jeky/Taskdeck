using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests;

public class AuditApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private bool _isAuthenticated;

    public AuditApiTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task AuditEndpoints_ShouldReturnUnauthorized_WhenNoToken()
    {
        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.GetAsync($"/api/audit/boards/{Guid.NewGuid()}"));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.GetAsync($"/api/audit/entities/Card/{Guid.NewGuid()}"));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.GetAsync($"/api/audit/users/{Guid.NewGuid()}"));
    }

    [Fact]
    public async Task GetBoardHistory_ShouldReturnOk_ForNewBoard()
    {
        var board = await CreateBoardAsync();

        var response = await _client.GetAsync($"/api/audit/boards/{board.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetBoardHistory_ShouldReturnBadRequest_WhenLimitIsInvalid()
    {
        var board = await CreateBoardAsync();

        var response = await _client.GetAsync($"/api/audit/boards/{board.Id}?limit=0");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("ValidationError");
    }

    [Fact]
    public async Task GetEntityHistory_ShouldReturnOk_ForAnyEntity()
    {
        await EnsureAuthenticatedAsync();

        var entityId = Guid.NewGuid();

        var response = await _client.GetAsync($"/api/audit/entities/Card/{entityId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetUserHistory_ShouldReturnOk_ForRegisteredUser()
    {
        var (_, _, _, userId) = await RegisterUserAsync("audituser");
        await EnsureAuthenticatedAsync();

        var response = await _client.GetAsync($"/api/audit/users/{userId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<BoardDto> CreateBoardAsync()
    {
        await EnsureAuthenticatedAsync();
        return await ApiTestHarness.CreateBoardAsync(_client, "audit-board", "Audit integration tests");
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

        await ApiTestHarness.AuthenticateAsync(_client, "audit-suite");
        _isAuthenticated = true;
    }
}
