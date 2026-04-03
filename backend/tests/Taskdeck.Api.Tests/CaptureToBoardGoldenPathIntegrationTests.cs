using System.Net;
using System.Net.Http.Json;
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

/// <summary>
/// Golden path integration tests that exercise the full capture-to-board pipeline:
/// capture input -> triage -> proposal generation -> review/approval -> board mutation.
/// This is the single most important integration test class in the system (GP-06).
/// </summary>
public class CaptureToBoardGoldenPathIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public CaptureToBoardGoldenPathIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HappyPath_CaptureTriageApproveExecute_ShouldCreateCardOnBoard()
    {
        // Arrange: authenticated user with a board and a column
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "golden-happy");
        var board = await ApiTestHarness.CreateBoardAsync(client, "golden-happy-board");

        var columnResponse = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Backlog", null, null));
        columnResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // Act 1: Create a capture item with structured checklist text
        var captureResponse = await client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(
                board.Id,
                "- [ ] Fix login bug"));
        captureResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var capture = await captureResponse.Content.ReadFromJsonAsync<CaptureItemDto>();
        capture.Should().NotBeNull();
        capture!.Status.Should().Be(CaptureStatus.New);

        // Act 2: Trigger triage (enqueues for worker processing)
        var triageResponse = await client.PostAsync($"/api/capture/items/{capture.Id}/triage", null);
        triageResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Act 3: Wait for worker to generate proposal
        var triaged = await WaitForCaptureStatusAsync(client, capture.Id, CaptureStatus.ProposalCreated);
        triaged.Status.Should().Be(CaptureStatus.ProposalCreated);
        triaged.Provenance.Should().NotBeNull();
        triaged.Provenance!.ProposalId.Should().NotBeNull();
        var proposalId = triaged.Provenance.ProposalId!.Value;

        // Act 4: Verify proposal exists with correct structure
        var proposalResponse = await client.GetAsync($"/api/automation/proposals/{proposalId}");
        proposalResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var proposal = await proposalResponse.Content.ReadFromJsonAsync<ProposalDto>();
        proposal.Should().NotBeNull();
        proposal!.Status.Should().Be(ProposalStatus.PendingReview);
        proposal.BoardId.Should().Be(board.Id);
        proposal.SourceType.Should().Be(ProposalSourceType.Queue);
        proposal.SourceReferenceId.Should().Be(capture.Id.ToString());
        proposal.Operations.Should().ContainSingle();
        proposal.Operations[0].ActionType.Should().Be("create");
        proposal.Operations[0].TargetType.Should().Be("card");

        // Act 5: Approve the proposal
        var approveResponse = await client.PostAsync($"/api/automation/proposals/{proposalId}/approve", null);
        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var approved = await approveResponse.Content.ReadFromJsonAsync<ProposalDto>();
        approved!.Status.Should().Be(ProposalStatus.Approved);

        // Act 6: Execute the proposal
        var executeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/automation/proposals/{proposalId}/execute");
        executeRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var executeResponse = await client.SendAsync(executeRequest);
        executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var executed = await executeResponse.Content.ReadFromJsonAsync<ProposalDto>();
        executed!.Status.Should().Be(ProposalStatus.Applied);
        executed.AppliedAt.Should().NotBeNull();

        // Assert: Card now exists on the board
        var cardsResponse = await client.GetAsync($"/api/boards/{board.Id}/cards");
        cardsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var cards = await cardsResponse.Content.ReadFromJsonAsync<List<CardDto>>();
        cards.Should().NotBeNull();
        cards!.Should().ContainSingle();
        cards[0].Title.Should().Be("Fix login bug");
        cards[0].BoardId.Should().Be(board.Id);

        // Assert: Capture item is now Converted
        var converted = await WaitForCaptureStatusAsync(client, capture.Id, CaptureStatus.Converted);
        converted.Status.Should().Be(CaptureStatus.Converted);
        converted.Provenance!.ProposalId.Should().Be(proposalId);
        converted.Provenance.ConvertedAt.Should().NotBeNull();

        // Assert: Provenance chain is intact
        converted.Provenance.CaptureItemId.Should().Be(capture.Id);
        converted.Provenance.BoardId.Should().Be(board.Id);
        converted.Provenance.Provider.Should().Be("Mock");
        converted.Provenance.SourceSurface.Should().Be("capture");
        converted.Provenance.CorrelationId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task MultiOperation_CaptureWithMultipleTasks_ShouldCreateMultipleCards()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "golden-multi");
        var board = await ApiTestHarness.CreateBoardAsync(client, "golden-multi-board");

        var columnResponse = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Backlog", null, null));
        columnResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // Capture with multiple checklist items
        var captureResponse = await client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(
                board.Id,
                """
                - [ ] Implement user authentication
                - [ ] Write API integration tests
                - [ ] Update deployment docs
                """));
        captureResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var capture = await captureResponse.Content.ReadFromJsonAsync<CaptureItemDto>();
        capture.Should().NotBeNull();

        // Triage and wait for proposal
        var triageResponse = await client.PostAsync($"/api/capture/items/{capture!.Id}/triage", null);
        triageResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var triaged = await WaitForCaptureStatusAsync(client, capture.Id, CaptureStatus.ProposalCreated);
        var proposalId = triaged.Provenance!.ProposalId!.Value;

        // Verify proposal has multiple operations
        var proposalResponse = await client.GetAsync($"/api/automation/proposals/{proposalId}");
        proposalResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var proposal = await proposalResponse.Content.ReadFromJsonAsync<ProposalDto>();
        proposal!.Operations.Should().HaveCount(3);
        proposal.Operations.Should().OnlyContain(op => op.ActionType == "create" && op.TargetType == "card");

        // Approve and execute
        var approveResponse = await client.PostAsync($"/api/automation/proposals/{proposalId}/approve", null);
        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var executeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/automation/proposals/{proposalId}/execute");
        executeRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var executeResponse = await client.SendAsync(executeRequest);
        executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // All three cards should exist on the board
        var cardsResponse = await client.GetAsync($"/api/boards/{board.Id}/cards");
        cardsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var cards = await cardsResponse.Content.ReadFromJsonAsync<List<CardDto>>();
        cards.Should().NotBeNull();
        cards!.Should().HaveCount(3);
        cards.Select(c => c.Title).Should().BeEquivalentTo(
            new[] { "Implement user authentication", "Write API integration tests", "Update deployment docs" });
    }

    [Fact]
    public async Task Rejection_ShouldLeaveBoardUnchanged()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "golden-reject");
        var board = await ApiTestHarness.CreateBoardAsync(client, "golden-reject-board");

        var columnResponse = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Backlog", null, null));
        columnResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // Create and triage capture
        var captureResponse = await client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(board.Id, "- [ ] Unwanted task"));
        captureResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var capture = await captureResponse.Content.ReadFromJsonAsync<CaptureItemDto>();

        var triageResponse = await client.PostAsync($"/api/capture/items/{capture!.Id}/triage", null);
        triageResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var triaged = await WaitForCaptureStatusAsync(client, capture.Id, CaptureStatus.ProposalCreated);
        var proposalId = triaged.Provenance!.ProposalId!.Value;

        // Reject the proposal
        var rejectResponse = await client.PostAsJsonAsync(
            $"/api/automation/proposals/{proposalId}/reject",
            new UpdateProposalStatusDto("Not needed"));
        rejectResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var rejected = await rejectResponse.Content.ReadFromJsonAsync<ProposalDto>();
        rejected!.Status.Should().Be(ProposalStatus.Rejected);

        // Board should have no cards
        var cardsResponse = await client.GetAsync($"/api/boards/{board.Id}/cards");
        cardsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var cards = await cardsResponse.Content.ReadFromJsonAsync<List<CardDto>>();
        cards.Should().NotBeNull();
        cards!.Should().BeEmpty();
    }

    [Fact]
    public async Task CrossUserIsolation_UserBCannotSeeOrApproveUserAProposal()
    {
        using var clientA = _factory.CreateClient();
        using var clientB = _factory.CreateClient();

        await ApiTestHarness.AuthenticateAsync(clientA, "golden-isolation-a");
        await ApiTestHarness.AuthenticateAsync(clientB, "golden-isolation-b");

        var boardA = await ApiTestHarness.CreateBoardAsync(clientA, "golden-isolation-board");

        var columnResponse = await clientA.PostAsJsonAsync(
            $"/api/boards/{boardA.Id}/columns",
            new CreateColumnDto(boardA.Id, "Backlog", null, null));
        columnResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // User A creates and triages a capture
        var captureResponse = await clientA.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(boardA.Id, "- [ ] Secret task for user A"));
        captureResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var capture = await captureResponse.Content.ReadFromJsonAsync<CaptureItemDto>();

        var triageResponse = await clientA.PostAsync($"/api/capture/items/{capture!.Id}/triage", null);
        triageResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var triaged = await WaitForCaptureStatusAsync(clientA, capture.Id, CaptureStatus.ProposalCreated);
        var proposalId = triaged.Provenance!.ProposalId!.Value;

        // User B cannot read the proposal
        var getResponse = await clientB.GetAsync($"/api/automation/proposals/{proposalId}");
        getResponse.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);

        // User B cannot approve the proposal
        var approveResponse = await clientB.PostAsync($"/api/automation/proposals/{proposalId}/approve", null);
        approveResponse.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);

        // User B cannot execute the proposal
        // First approve as User A so we can test execution isolation
        var approveAsA = await clientA.PostAsync($"/api/automation/proposals/{proposalId}/approve", null);
        approveAsA.StatusCode.Should().Be(HttpStatusCode.OK);

        var executeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/automation/proposals/{proposalId}/execute");
        executeRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var executeResponse = await clientB.SendAsync(executeRequest);
        executeResponse.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AuditTrail_ShouldRecordBoardMutationAfterExecution()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "golden-audit");
        var board = await ApiTestHarness.CreateBoardAsync(client, "golden-audit-board");

        var columnResponse = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Backlog", null, null));
        columnResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // Full pipeline: capture -> triage -> approve -> execute
        var captureResponse = await client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(board.Id, "- [ ] Audit trail test card"));
        captureResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var capture = await captureResponse.Content.ReadFromJsonAsync<CaptureItemDto>();

        await client.PostAsync($"/api/capture/items/{capture!.Id}/triage", null);
        var triaged = await WaitForCaptureStatusAsync(client, capture.Id, CaptureStatus.ProposalCreated);
        var proposalId = triaged.Provenance!.ProposalId!.Value;

        await client.PostAsync($"/api/automation/proposals/{proposalId}/approve", null);

        var executeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/automation/proposals/{proposalId}/execute");
        executeRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        await client.SendAsync(executeRequest);

        // Verify audit trail records the card creation
        var auditResponse = await client.GetAsync($"/api/audit/boards/{board.Id}");
        auditResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var auditLogs = await auditResponse.Content.ReadFromJsonAsync<List<AuditLogDto>>();
        auditLogs.Should().NotBeNull();

        // There should be at least one audit entry for card creation on this board
        auditLogs!.Should().Contain(log =>
            string.Equals(log.EntityType, "card", StringComparison.OrdinalIgnoreCase) &&
            log.Action == AuditAction.Created);
    }

    [Fact]
    public async Task ProvenanceIntegrity_BackwardTraceable_CardToProposalToCapture()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "golden-provenance");
        var board = await ApiTestHarness.CreateBoardAsync(client, "golden-provenance-board");

        var columnResponse = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Backlog", null, null));
        columnResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var captureResponse = await client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(board.Id, "- [ ] Provenance test card"));
        captureResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var capture = await captureResponse.Content.ReadFromJsonAsync<CaptureItemDto>();

        await client.PostAsync($"/api/capture/items/{capture!.Id}/triage", null);
        var triaged = await WaitForCaptureStatusAsync(client, capture.Id, CaptureStatus.ProposalCreated);
        var proposalId = triaged.Provenance!.ProposalId!.Value;

        await client.PostAsync($"/api/automation/proposals/{proposalId}/approve", null);
        var executeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/automation/proposals/{proposalId}/execute");
        executeRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        await client.SendAsync(executeRequest);

        // Wait for capture to be marked Converted
        var converted = await WaitForCaptureStatusAsync(client, capture.Id, CaptureStatus.Converted);

        // Verify the full provenance chain
        // 1. Capture -> Proposal link via provenance
        converted.Provenance!.CaptureItemId.Should().Be(capture.Id);
        converted.Provenance.ProposalId.Should().Be(proposalId);
        converted.Provenance.ConvertedAt.Should().NotBeNull();
        converted.Provenance.CorrelationId.Should().NotBeNullOrWhiteSpace();

        // 2. Proposal -> Capture link via SourceReferenceId
        var proposalResponse = await client.GetAsync($"/api/automation/proposals/{proposalId}");
        proposalResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var proposal = await proposalResponse.Content.ReadFromJsonAsync<ProposalDto>();
        proposal!.SourceReferenceId.Should().Be(capture.Id.ToString());
        proposal.SourceType.Should().Be(ProposalSourceType.Queue);
        proposal.CorrelationId.Should().NotBeNullOrWhiteSpace();

        // 3. Persisted payload provenance is consistent
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskdeckDbContext>();
        var persistedItem = await db.LlmRequests.SingleAsync(r => r.Id == capture.Id);
        var payload = CaptureRequestContract.ParsePayload(persistedItem.Payload, allowServerAttributionFields: true);
        payload.IsSuccess.Should().BeTrue();
        payload.Value.Provenance.Should().NotBeNull();
        payload.Value.Provenance!.ProposalId.Should().Be(proposalId);
        payload.Value.Provenance.ConvertedAt.Should().NotBeNull();
        payload.Value.Provenance.RequestedByUserId.Should().Be(user.UserId);
    }

    [Fact]
    public async Task TriageFailure_NoBoardId_ShouldFailDeterministically()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "golden-fail");

        // Capture without a board should fail during triage
        var captureResponse = await client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(null, "- [ ] Task without a board"));
        captureResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var capture = await captureResponse.Content.ReadFromJsonAsync<CaptureItemDto>();

        var triageResponse = await client.PostAsync($"/api/capture/items/{capture!.Id}/triage", null);
        triageResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var failed = await WaitForCaptureStatusAsync(client, capture.Id, CaptureStatus.Failed);
        failed.Status.Should().Be(CaptureStatus.Failed);
        failed.Provenance.Should().NotBeNull();
        failed.Provenance!.ProposalId.Should().BeNull();
    }

    private static async Task<CaptureItemDto> WaitForCaptureStatusAsync(
        HttpClient client,
        Guid itemId,
        CaptureStatus expectedStatus)
    {
        return await ApiTestHarness.PollUntilAsync(
            async () =>
            {
                var response = await client.GetAsync($"/api/capture/items/{itemId}");
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                var item = await response.Content.ReadFromJsonAsync<CaptureItemDto>();
                item.Should().NotBeNull();
                return item!;
            },
            item => item.Status == expectedStatus ||
                    (item.Status == CaptureStatus.Failed && expectedStatus != CaptureStatus.Failed),
            $"capture item {itemId} status to become {expectedStatus}",
            maxAttempts: 40,
            interval: TimeSpan.FromMilliseconds(250),
            diagnostics: item => item is null
                ? "item=null"
                : $"status={item.Status}, proposalId={item.Provenance?.ProposalId?.ToString() ?? "null"}");
    }
}
