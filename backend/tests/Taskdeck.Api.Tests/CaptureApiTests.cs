using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Api.Tests;

public class CaptureApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CaptureApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CaptureEndpoints_ShouldReturnUnauthorized_WhenNoToken()
    {
        var itemId = Guid.NewGuid();

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.PostAsJsonAsync("/api/capture/items", new CreateCaptureItemDto(null, "capture")));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.GetAsync("/api/capture/items"));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.GetAsync($"/api/capture/items/{itemId}"));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.PostAsync($"/api/capture/items/{itemId}/ignore", null));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.PostAsync($"/api/capture/items/{itemId}/cancel", null));

        await ApiTestHarness.AssertUnauthorizedAsync(
            await _client.PostAsync($"/api/capture/items/{itemId}/triage", null));
    }

    [Fact]
    public async Task Create_ShouldReturnCreated_WithValidPayload()
    {
        await AuthenticateAsAsync("capture-create");
        var board = await ApiTestHarness.CreateBoardAsync(_client, "capture-create-board");

        var response = await _client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(board.Id, "capture this note", "paste"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await response.Content.ReadFromJsonAsync<CaptureItemDto>();
        dto.Should().NotBeNull();
        dto!.BoardId.Should().Be(board.Id);
        dto.Source.Should().Be(CaptureSource.Paste);
        dto.Status.Should().Be(CaptureStatus.New);
        dto.RawText.Should().Be("capture this note");
        dto.TextExcerpt.Should().Contain("capture this note");
    }

    [Fact]
    public async Task List_ShouldReturnCaptureItemsOnly_AndExcludeRawText()
    {
        await AuthenticateAsAsync("capture-list");

        var createCaptureResponse = await _client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(null, "capture-only payload"));
        createCaptureResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createQueueResponse = await _client.PostAsJsonAsync(
            "/api/llm-queue",
            new CreateLlmRequestDto("summarize", "normal queue payload"));
        createQueueResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await _client.GetAsync("/api/capture/items");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync();
        var list = JsonSerializer.Deserialize<List<CaptureItemSummaryDto>>(
            raw,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        list.Should().NotBeNull();
        var captures = list!;
        captures.Should().HaveCount(1);
        captures[0].TextExcerpt.Should().Contain("capture-only payload");
        raw.ToLowerInvariant().Should().NotContain("rawtext");
    }

    [Fact]
    public async Task List_ShouldReturnBadRequest_WhenStatusFilterIsInvalid()
    {
        await AuthenticateAsAsync("capture-list-invalid-status");

        var response = await _client.GetAsync("/api/capture/items?status=not-a-real-status");

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.BadRequest, "ValidationError");
    }

    [Fact]
    public async Task GetById_ShouldReturnForbidden_WhenItemBelongsToDifferentUser()
    {
        using var ownerClient = _factory.CreateClient();
        using var outsiderClient = _factory.CreateClient();

        await ApiTestHarness.AuthenticateAsync(ownerClient, "capture-owner");
        await ApiTestHarness.AuthenticateAsync(outsiderClient, "capture-outsider");

        var createResponse = await ownerClient.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(null, "owner capture payload"));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<CaptureItemDto>();
        created.Should().NotBeNull();

        var response = await outsiderClient.GetAsync($"/api/capture/items/{created!.Id}");
        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    [Fact]
    public async Task GetById_ShouldReturnNotFound_WhenItemDoesNotExist()
    {
        await AuthenticateAsAsync("capture-missing");

        var response = await _client.GetAsync($"/api/capture/items/{Guid.NewGuid()}");

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.NotFound, "NotFound");
    }

    [Fact]
    public async Task Ignore_ShouldBeIdempotent()
    {
        await AuthenticateAsAsync("capture-ignore");

        var createResponse = await _client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(null, "ignore payload"));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<CaptureItemDto>();
        created.Should().NotBeNull();

        var first = await _client.PostAsync($"/api/capture/items/{created!.Id}/ignore", null);
        var second = await _client.PostAsync($"/api/capture/items/{created.Id}/ignore", null);

        first.StatusCode.Should().Be(HttpStatusCode.NoContent);
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Ignore_ShouldReturnForbidden_WhenItemBelongsToDifferentUser()
    {
        using var ownerClient = _factory.CreateClient();
        using var outsiderClient = _factory.CreateClient();

        await ApiTestHarness.AuthenticateAsync(ownerClient, "capture-ignore-owner");
        await ApiTestHarness.AuthenticateAsync(outsiderClient, "capture-ignore-outsider");

        var createResponse = await ownerClient.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(null, "owner ignore payload"));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<CaptureItemDto>();
        created.Should().NotBeNull();

        var response = await outsiderClient.PostAsync($"/api/capture/items/{created!.Id}/ignore", null);
        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    [Fact]
    public async Task Cancel_ShouldReturnNoContent_WhenCaptureExists()
    {
        await AuthenticateAsAsync("capture-cancel");

        var createResponse = await _client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(null, "cancel payload"));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<CaptureItemDto>();
        created.Should().NotBeNull();

        var response = await _client.PostAsync($"/api/capture/items/{created!.Id}/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Triage_ShouldReturnAcceptedAndTriagingState()
    {
        await AuthenticateAsAsync("capture-triage");

        var createResponse = await _client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(null, "triage payload"));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<CaptureItemDto>();
        created.Should().NotBeNull();

        var response = await _client.PostAsync($"/api/capture/items/{created!.Id}/triage", null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var triageResult = await response.Content.ReadFromJsonAsync<CaptureTriageEnqueueResultDto>();
        triageResult.Should().NotBeNull();
        triageResult!.Status.Should().Be(CaptureStatus.Triaging);
        triageResult.AlreadyTriaging.Should().BeFalse();

        var detailResponse = await _client.GetAsync($"/api/capture/items/{created.Id}");
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await detailResponse.Content.ReadFromJsonAsync<CaptureItemDto>();
        detail.Should().NotBeNull();
        detail!.Status.Should().Be(CaptureStatus.Triaging);
    }

    [Fact]
    public async Task Triage_ShouldBeIdempotent_WhenAlreadyTriaging()
    {
        await AuthenticateAsAsync("capture-triage-repeat");

        var createResponse = await _client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(null, "triage repeat payload"));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<CaptureItemDto>();
        created.Should().NotBeNull();

        var first = await _client.PostAsync($"/api/capture/items/{created!.Id}/triage", null);
        var second = await _client.PostAsync($"/api/capture/items/{created.Id}/triage", null);

        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        second.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var secondResult = await second.Content.ReadFromJsonAsync<CaptureTriageEnqueueResultDto>();
        secondResult.Should().NotBeNull();
        secondResult!.Status.Should().Be(CaptureStatus.Triaging);
        secondResult.AlreadyTriaging.Should().BeTrue();
    }

    [Fact]
    public async Task Triage_ShouldReturnConflict_WhenCaptureIsIgnored()
    {
        await AuthenticateAsAsync("capture-triage-conflict");

        var createResponse = await _client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(null, "triage conflict payload"));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<CaptureItemDto>();
        created.Should().NotBeNull();

        var ignoreResponse = await _client.PostAsync($"/api/capture/items/{created!.Id}/ignore", null);
        ignoreResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await _client.PostAsync($"/api/capture/items/{created.Id}/triage", null);

        await ApiTestHarness.AssertErrorContractAsync(response, HttpStatusCode.Conflict, "Conflict");
    }

    [Fact]
    public async Task Triage_ShouldReturnForbidden_WhenCaptureBelongsToDifferentUser()
    {
        using var ownerClient = _factory.CreateClient();
        using var outsiderClient = _factory.CreateClient();

        await ApiTestHarness.AuthenticateAsync(ownerClient, "capture-triage-owner");
        await ApiTestHarness.AuthenticateAsync(outsiderClient, "capture-triage-outsider");

        var createResponse = await ownerClient.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(null, "owner triage payload"));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<CaptureItemDto>();
        created.Should().NotBeNull();

        var response = await outsiderClient.PostAsync($"/api/capture/items/{created!.Id}/triage", null);
        await ApiTestHarness.AssertForbiddenAsync(response);
    }

    private async Task AuthenticateAsAsync(string stem)
    {
        await ApiTestHarness.AuthenticateAsync(_client, stem);
    }
}
