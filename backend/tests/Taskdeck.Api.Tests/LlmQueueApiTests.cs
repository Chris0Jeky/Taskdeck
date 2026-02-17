using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests;

public class LlmQueueApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private bool _isAuthenticated;

    public LlmQueueApiTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task LlmQueueEndpoints_ShouldReturnUnauthorized_WhenNoToken()
    {
        var requestId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.PostAsJsonAsync("/api/llm-queue", new CreateLlmRequestDto(userId, "summarize", "payload")));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.GetAsync($"/api/llm-queue/user/{userId}"));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.GetAsync("/api/llm-queue/status/Pending"));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.GetAsync("/api/llm-queue/stats"));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.PostAsync($"/api/llm-queue/{requestId}/cancel?userId={userId}", null));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.PostAsync("/api/llm-queue/process-next", null));
    }

    [Fact]
    public async Task AddToQueue_ShouldReturnOk_WithValidData()
    {
        await EnsureAuthenticatedAsync();
        var (_, _, _, userId) = await RegisterUserAsync("llmuser");
        var boardId = await CreateOwnedBoardAsync("llmboard", userId);

        var dto = new CreateLlmRequestDto(userId, "summarize", "payload data", boardId);

        var response = await _client.PostAsJsonAsync("/api/llm-queue", dto);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var request = await response.Content.ReadFromJsonAsync<LlmRequestDto>();
        request.Should().NotBeNull();
        request!.UserId.Should().Be(userId);
        request.BoardId.Should().Be(boardId);
        request.RequestType.Should().Be("summarize");
    }

    [Fact]
    public async Task AddToQueue_ShouldReturnNotFound_WhenUserDoesNotExist()
    {
        await EnsureAuthenticatedAsync();
        var dto = new CreateLlmRequestDto(Guid.NewGuid(), "summarize", "payload");

        var response = await _client.PostAsJsonAsync("/api/llm-queue", dto);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task AddToQueue_ShouldReturnNotFound_WhenBoardDoesNotExist()
    {
        await EnsureAuthenticatedAsync();
        var (_, _, _, userId) = await RegisterUserAsync("llmnobrd");

        var dto = new CreateLlmRequestDto(userId, "summarize", "payload", Guid.NewGuid());

        var response = await _client.PostAsJsonAsync("/api/llm-queue", dto);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task GetUserQueue_ShouldReturnOk_WhenNoRequests()
    {
        await EnsureAuthenticatedAsync();
        var (_, _, _, userId) = await RegisterUserAsync("llmempty");

        var response = await _client.GetAsync($"/api/llm-queue/user/{userId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetByStatus_ShouldReturnBadRequest_WhenStatusIsInvalid()
    {
        await EnsureAuthenticatedAsync();
        var response = await _client.GetAsync("/api/llm-queue/status/InvalidStatus");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("ValidationError");
    }

    [Fact]
    public async Task GetByStatus_ShouldReturnOk_WithValidStatus()
    {
        await EnsureAuthenticatedAsync();
        var response = await _client.GetAsync("/api/llm-queue/status/Pending");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetQueueStats_ShouldReturnOk()
    {
        await EnsureAuthenticatedAsync();
        var response = await _client.GetAsync("/api/llm-queue/stats");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CancelRequest_ShouldReturnNotFound_WhenRequestDoesNotExist()
    {
        await EnsureAuthenticatedAsync();
        var (_, _, _, userId) = await RegisterUserAsync("llmcancel");

        var response = await _client.PostAsync(
            $"/api/llm-queue/{Guid.NewGuid()}/cancel?userId={userId}", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("NotFound");
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

    private async Task<Guid> CreateOwnedBoardAsync(string stem, Guid ownerId)
    {
        await EnsureAuthenticatedAsync();

        var response = await _client.PostAsJsonAsync(
            $"/api/import/boards?userId={ownerId}",
            new ImportBoardDto(
                $"{stem}-{Guid.NewGuid():N}",
                null,
                Array.Empty<ImportColumnDto>(),
                Array.Empty<ImportCardDto>(),
                Array.Empty<ImportLabelDto>()));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ImportResultDto>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.BoardId.Should().NotBeNull();
        return result.BoardId!.Value;
    }

    private async Task EnsureAuthenticatedAsync()
    {
        if (_isAuthenticated)
        {
            return;
        }

        await ApiTestHarness.AuthenticateAsync(_client, "llmqueue-suite");
        _isAuthenticated = true;
    }
}
