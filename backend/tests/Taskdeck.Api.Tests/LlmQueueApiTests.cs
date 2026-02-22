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
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private bool _isAuthenticated;
    private Guid? _authenticatedUserId;

    public LlmQueueApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task LlmQueueEndpoints_ShouldReturnUnauthorized_WhenNoToken()
    {
        var requestId = Guid.NewGuid();

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.PostAsJsonAsync("/api/llm-queue", new CreateLlmRequestDto("summarize", "payload")));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.GetAsync("/api/llm-queue/user"));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.GetAsync("/api/llm-queue/status/Pending"));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.GetAsync("/api/llm-queue/stats"));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.PostAsync($"/api/llm-queue/{requestId}/cancel", null));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.PostAsync("/api/llm-queue/process-next", null));
    }

    [Fact]
    public async Task AddToQueue_ShouldReturnOk_WithValidData()
    {
        await EnsureAuthenticatedAsync();
        var boardId = await CreateOwnedBoardAsync("llmboard");

        var dto = new CreateLlmRequestDto("summarize", "payload data", boardId);

        var response = await _client.PostAsJsonAsync("/api/llm-queue", dto);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var request = await response.Content.ReadFromJsonAsync<LlmRequestDto>();
        request.Should().NotBeNull();
        _authenticatedUserId.Should().NotBeNull();
        request!.UserId.Should().Be(_authenticatedUserId!.Value);
        request.BoardId.Should().Be(boardId);
        request.RequestType.Should().Be("summarize");
    }

    [Fact]
    public async Task GetUserQueue_ShouldOnlyReturnCurrentUserRequests()
    {
        var owner = await AuthenticateAsAsync("llmowner");
        var boardId = await CreateOwnedBoardAsync("llm-owner-board");

        var createResponse = await _client.PostAsJsonAsync(
            "/api/llm-queue",
            new CreateLlmRequestDto("summarize", "owner payload", boardId));
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        await AuthenticateAsAsync("llmother");
        var response = await _client.GetAsync("/api/llm-queue/user");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var requests = await response.Content.ReadFromJsonAsync<List<LlmRequestDto>>();
        requests.Should().NotBeNull();
        requests.Should().NotContain(request => request.UserId == owner.UserId);
    }

    [Fact]
    public async Task AddToQueue_ShouldReturnNotFound_WhenBoardDoesNotExist()
    {
        await EnsureAuthenticatedAsync();

        var dto = new CreateLlmRequestDto("summarize", "payload", Guid.NewGuid());

        var response = await _client.PostAsJsonAsync("/api/llm-queue", dto);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task AddToQueue_ShouldReturnForbidden_WhenBoardBelongsToDifferentUser()
    {
        using var ownerClient = _factory.CreateClient();
        using var outsiderClient = _factory.CreateClient();

        await ApiTestHarness.AuthenticateAsync(ownerClient, "llm-queue-board-owner");
        await ApiTestHarness.AuthenticateAsync(outsiderClient, "llm-queue-board-outsider");
        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "llm-queue-protected-board");

        var response = await outsiderClient.PostAsJsonAsync(
            "/api/llm-queue",
            new CreateLlmRequestDto("summarize", "cross-user payload", board.Id));

        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    [Fact]
    public async Task GetUserQueue_ShouldReturnOk_WhenNoRequests()
    {
        await EnsureAuthenticatedAsync();

        var response = await _client.GetAsync("/api/llm-queue/user");

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
        var response = await _client.PostAsync($"/api/llm-queue/{Guid.NewGuid()}/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var errorPayload = await response.Content.ReadFromJsonAsync<JsonElement>();
        errorPayload.GetProperty("errorCode").GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task CancelRequest_ShouldReturnForbidden_WhenRequestBelongsToDifferentUser()
    {
        await AuthenticateAsAsync("llm-cancel-owner");
        var boardId = await CreateOwnedBoardAsync("llm-cancel-board");

        var createResponse = await _client.PostAsJsonAsync(
            "/api/llm-queue",
            new CreateLlmRequestDto("summarize", "owner payload", boardId));
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var createdRequest = await createResponse.Content.ReadFromJsonAsync<LlmRequestDto>();
        createdRequest.Should().NotBeNull();

        await AuthenticateAsAsync("llm-cancel-other");
        var response = await _client.PostAsync($"/api/llm-queue/{createdRequest!.Id}/cancel", null);

        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    private async Task<Guid> CreateOwnedBoardAsync(string stem)
    {
        await EnsureAuthenticatedAsync();

        var response = await _client.PostAsJsonAsync(
            "/api/import/boards",
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

    private async Task<TestUserContext> AuthenticateAsAsync(string stem)
    {
        var context = await ApiTestHarness.AuthenticateAsync(_client, stem);
        _isAuthenticated = true;
        _authenticatedUserId = context.UserId;
        return context;
    }

    private async Task EnsureAuthenticatedAsync()
    {
        if (_isAuthenticated)
        {
            return;
        }

        await AuthenticateAsAsync("llmqueue-suite");
    }
}
