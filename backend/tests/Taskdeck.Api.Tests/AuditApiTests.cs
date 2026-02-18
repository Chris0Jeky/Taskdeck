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
            await _client.GetAsync("/api/audit/users/me"));
    }

    [Fact]
    public async Task GetBoardHistory_ShouldReturnOk_ForAccessibleBoard()
    {
        var board = await CreateBoardAsync();

        var response = await _client.GetAsync($"/api/audit/boards/{board.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetBoardHistory_ShouldReturnForbidden_WhenUserHasNoBoardAccess()
    {
        await AuthenticateAsAsync("audit-board-owner");
        var board = await ApiTestHarness.CreateBoardAsync(_client, "audit-private-board", "Audit security test");

        await AuthenticateAsAsync("audit-board-outsider");
        var response = await _client.GetAsync($"/api/audit/boards/{board.Id}");

        await ApiTestHarness.AssertForbiddenAsync(response);
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
    public async Task GetUserHistory_ShouldReturnOk_ForCurrentUser()
    {
        await EnsureAuthenticatedAsync();

        var response = await _client.GetAsync("/api/audit/users/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<BoardDto> CreateBoardAsync()
    {
        await EnsureAuthenticatedAsync();
        return await ApiTestHarness.CreateBoardAsync(_client, "audit-board", "Audit integration tests");
    }

    private async Task<TestUserContext> AuthenticateAsAsync(string stem)
    {
        var context = await ApiTestHarness.AuthenticateAsync(_client, stem);
        _isAuthenticated = true;
        return context;
    }

    private async Task EnsureAuthenticatedAsync()
    {
        if (_isAuthenticated)
        {
            return;
        }

        await AuthenticateAsAsync("audit-suite");
    }
}
