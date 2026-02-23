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

    private async Task AuthenticateAsAsync(string stem)
    {
        await ApiTestHarness.AuthenticateAsync(_client, stem);
    }
}
