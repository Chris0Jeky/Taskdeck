using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Infrastructure.Persistence;
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
    public async Task Triage_ShouldCreateProposalAndMarkCaptureAsProposalCreated()
    {
        var user = await ApiTestHarness.AuthenticateAsync(_client, "capture-triage-proposal");
        var board = await ApiTestHarness.CreateBoardAsync(_client, "capture-triage-proposal-board");
        var createColumnResponse = await _client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Inbox", null, null));
        createColumnResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createResponse = await _client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(
                board.Id,
                """
                - [ ] Write API tests for capture triage
                - [ ] Update implementation docs
                """));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<CaptureItemDto>();
        created.Should().NotBeNull();

        var triageResponse = await _client.PostAsync($"/api/capture/items/{created!.Id}/triage", null);
        triageResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var finalItem = await WaitForCaptureStatusAsync(created.Id, CaptureStatus.ProposalCreated);
        finalItem.Status.Should().Be(CaptureStatus.ProposalCreated);
        finalItem.Provenance.Should().NotBeNull();
        finalItem.Provenance!.CaptureItemId.Should().Be(created.Id);
        finalItem.Provenance.ProposalId.Should().NotBeNull();
        finalItem.Provenance.ProposalId.Should().NotBe(Guid.Empty);
        finalItem.Provenance.PromptVersion.Should().Be(CaptureTriageOutputContract.PromptVersionV1);

        var proposalsResponse = await _client.GetAsync($"/api/automation/proposals?boardId={board.Id}&status=PendingReview&limit=20");
        proposalsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var proposals = await proposalsResponse.Content.ReadFromJsonAsync<List<ProposalDto>>();
        proposals.Should().NotBeNull();
        proposals!.Should().Contain(p =>
            p.RequestedByUserId == user.UserId &&
            p.SourceType == ProposalSourceType.Queue &&
            p.SourceReferenceId == created.Id.ToString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var persistedItem = await db.LlmRequests.SingleAsync(request => request.Id == created.Id);
        var payload = CaptureRequestContract.ParsePayload(persistedItem.Payload);
        payload.IsSuccess.Should().BeTrue();
        payload.Value.Provenance.Should().NotBeNull();
        payload.Value.Provenance!.PromptVersion.Should().Be(CaptureTriageOutputContract.PromptVersionV1);
    }

    [Fact]
    public async Task Triage_ShouldFailDeterministically_WhenCaptureHasNoBoard()
    {
        await AuthenticateAsAsync("capture-triage-fail");

        var createResponse = await _client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(
                null,
                """
                - [ ] Draft release notes
                - [ ] Review checklist
                """));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<CaptureItemDto>();
        created.Should().NotBeNull();

        var triageResponse = await _client.PostAsync($"/api/capture/items/{created!.Id}/triage", null);
        triageResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var failedItem = await WaitForCaptureStatusAsync(created.Id, CaptureStatus.Failed);
        failedItem.Status.Should().Be(CaptureStatus.Failed);
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

    private async Task<CaptureItemDto> WaitForCaptureStatusAsync(Guid itemId, CaptureStatus expectedStatus)
    {
        return await ApiTestHarness.PollUntilAsync(
            async () =>
            {
                var response = await _client.GetAsync($"/api/capture/items/{itemId}");
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                var item = await response.Content.ReadFromJsonAsync<CaptureItemDto>();
                item.Should().NotBeNull();
                return item!;
            },
            item => item.Status == expectedStatus || (item.Status == CaptureStatus.Failed && expectedStatus != CaptureStatus.Failed),
            $"capture item {itemId} status to become {expectedStatus}",
            maxAttempts: 40,
            interval: TimeSpan.FromMilliseconds(250),
            diagnostics: item => item is null
                ? "item=null"
                : $"status={item.Status}, proposalId={item.Provenance?.ProposalId?.ToString() ?? "null"}, triageRunId={item.Provenance?.TriageRunId?.ToString() ?? "null"}");
    }
}
