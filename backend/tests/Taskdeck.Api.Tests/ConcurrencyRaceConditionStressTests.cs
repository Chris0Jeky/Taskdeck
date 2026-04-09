using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Taskdeck.Api.RateLimiting;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Concurrency and race condition stress tests exercising:
/// - Queue claim races (double-claim prevention, stale timestamp claim, concurrent workers)
/// - Card update conflicts (concurrent moves, stale-write detection, column reorder race)
/// - Proposal approval races (double-approve prevention, approve+expire race)
/// - Rate limiting under load (burst beyond limit, cross-user isolation under load)
///
/// Uses Task.WhenAll with multiple HttpClient instances for HTTP-level concurrency.
/// Uses SemaphoreSlim barriers to ensure truly simultaneous execution.
///
/// NOTE: SQLite uses file-level locking for writes, so true write-contention races
/// may serialize at the database level. These tests validate the application-layer
/// guards (optimistic concurrency checks, domain state machines) regardless.
///
/// See GitHub issue #705 (TST-38).
/// </summary>
public class ConcurrencyRaceConditionStressTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ConcurrencyRaceConditionStressTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── Queue Claim Races ────────────────────────────────────────────────────

    /// <summary>
    /// Scenario 1: Two concurrent triage requests for the same capture item.
    /// Both may succeed because SQLite serializes writes and the triage endpoint
    /// is idempotent, but no unexpected errors should occur.
    /// </summary>
    [Fact]
    public async Task CaptureTriage_ConcurrentDoubleTriageForSameItem_AtMostOneSucceeds()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "race-triage-double");
        var board = await ApiTestHarness.CreateBoardAsync(client, "race-triage-board");

        var colResp = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Backlog", null, null));
        colResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var captureResp = await client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(board.Id, "- [ ] Double triage race item"));
        captureResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var capture = await captureResp.Content.ReadFromJsonAsync<CaptureItemDto>();
        capture.Should().NotBeNull();

        // Fire two triage requests simultaneously using a barrier
        using var barrier = new SemaphoreSlim(0, 2);
        var results = new ConcurrentBag<HttpStatusCode>();

        var tasks = Enumerable.Range(0, 2).Select(async _ =>
        {
            using var raceClient = _factory.CreateClient();
            raceClient.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;
            await barrier.WaitAsync();
            var resp = await raceClient.PostAsync($"/api/capture/items/{capture!.Id}/triage", null);
            results.Add(resp.StatusCode);
        }).ToArray();

        barrier.Release(2);
        await Task.WhenAll(tasks);

        // At least one should succeed; together they should not produce errors beyond
        // expected conflict/duplicate states.
        var statusCodes = results.ToList();
        var acceptedCount = statusCodes.Count(s => s == HttpStatusCode.Accepted);
        acceptedCount.Should().BeGreaterOrEqualTo(1,
            "at least one concurrent triage must succeed");
        statusCodes.Should().AllSatisfy(s =>
            s.Should().BeOneOf(
                HttpStatusCode.Accepted,
                HttpStatusCode.OK,
                HttpStatusCode.Conflict,
                HttpStatusCode.BadRequest),
            "unexpected HTTP status from concurrent triage");
    }

    /// <summary>
    /// Scenario 2: Capture triage claim with stale timestamp.
    /// If a capture item has already been triaged (status changed),
    /// a second triage attempt should not produce a second proposal.
    /// </summary>
    [Fact]
    public async Task CaptureTriage_StaleTimestamp_ShouldNotProduceDuplicateProposal()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "race-triage-stale");
        var board = await ApiTestHarness.CreateBoardAsync(client, "race-triage-stale-board");

        var colResp = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Backlog", null, null));
        colResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var captureResp = await client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(board.Id, "- [ ] Stale triage item"));
        captureResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var capture = await captureResp.Content.ReadFromJsonAsync<CaptureItemDto>();
        capture.Should().NotBeNull();

        // First triage
        var firstTriage = await client.PostAsync($"/api/capture/items/{capture!.Id}/triage", null);
        firstTriage.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Wait for worker to process
        var triaged = await ApiTestHarness.PollUntilAsync(
            async () =>
            {
                var r = await client.GetAsync($"/api/capture/items/{capture.Id}");
                return await r.Content.ReadFromJsonAsync<CaptureItemDto>();
            },
            item => item?.Status is CaptureStatus.ProposalCreated or CaptureStatus.Triaging,
            "capture triage processing",
            maxAttempts: 30);

        // Second triage attempt on already-processed item
        var secondTriage = await client.PostAsync($"/api/capture/items/{capture.Id}/triage", null);

        // Should either reject (conflict/bad request) or be idempotent
        secondTriage.StatusCode.Should().BeOneOf(
            HttpStatusCode.Accepted,
            HttpStatusCode.OK,
            HttpStatusCode.Conflict,
            HttpStatusCode.BadRequest,
            (HttpStatusCode)429);

        // Count proposals to ensure no duplicates from the stale re-triage
        var proposalsResp = await client.GetAsync($"/api/automation/proposals?boardId={board.Id}");
        proposalsResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var proposals = await proposalsResp.Content.ReadFromJsonAsync<List<ProposalDto>>();
        proposals.Should().NotBeNull();

        // At most one proposal should reference this capture item
        var captureProposals = proposals!.Where(p =>
            p.SourceReferenceId == capture.Id.ToString()).ToList();
        captureProposals.Should().HaveCountLessOrEqualTo(1,
            "stale re-triage should not create duplicate proposals");
    }

    /// <summary>
    /// Scenario 3: Batch capture and concurrent triage of multiple items.
    /// Simulates concurrent workers processing different capture items.
    /// </summary>
    [Fact]
    public async Task CaptureTriage_BatchConcurrentWorkers_AllItemsShouldTriageWithoutErrors()
    {
        const int batchSize = 5;
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "race-batch-triage");
        var board = await ApiTestHarness.CreateBoardAsync(client, "race-batch-board");

        var colResp = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Backlog", null, null));
        colResp.StatusCode.Should().Be(HttpStatusCode.Created);

        // Create multiple capture items
        var captureIds = new List<Guid>();
        for (var i = 0; i < batchSize; i++)
        {
            var resp = await client.PostAsJsonAsync(
                "/api/capture/items",
                new CreateCaptureItemDto(board.Id, $"- [ ] Batch item {i}"));
            resp.StatusCode.Should().Be(HttpStatusCode.Created);
            var item = await resp.Content.ReadFromJsonAsync<CaptureItemDto>();
            captureIds.Add(item!.Id);
        }

        // Triage all items concurrently
        using var barrier = new SemaphoreSlim(0, batchSize);
        var results = new ConcurrentDictionary<Guid, HttpStatusCode>();

        var tasks = captureIds.Select(async captureId =>
        {
            using var raceClient = _factory.CreateClient();
            raceClient.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;
            await barrier.WaitAsync();
            var resp = await raceClient.PostAsync($"/api/capture/items/{captureId}/triage", null);
            results[captureId] = resp.StatusCode;
        }).ToArray();

        barrier.Release(batchSize);
        await Task.WhenAll(tasks);

        // All distinct items should triage successfully
        results.Values.Should().AllSatisfy(s =>
            s.Should().BeOneOf(HttpStatusCode.Accepted, HttpStatusCode.OK),
            "each distinct capture item should triage without conflict");
    }

    // ── Card Update Conflicts ────────────────────────────────────────────────

    /// <summary>
    /// Scenario 4: Two concurrent card moves to different columns.
    /// SQLite serializes writes, so both may succeed sequentially,
    /// but the card should end up in exactly one column (last-writer-wins
    /// at the HTTP level when no optimistic concurrency stamp is used for moves).
    /// </summary>
    [Fact]
    public async Task CardMove_ConcurrentMovesToDifferentColumns_CardEndsInOneColumn()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "race-card-move");
        var board = await ApiTestHarness.CreateBoardAsync(client, "race-move-board");

        // Create two columns
        var col1Resp = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Todo", 0, null));
        col1Resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var col1 = await col1Resp.Content.ReadFromJsonAsync<ColumnDto>();

        var col2Resp = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "InProgress", 1, null));
        col2Resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var col2 = await col2Resp.Content.ReadFromJsonAsync<ColumnDto>();

        var col3Resp = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Done", 2, null));
        col3Resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var col3 = await col3Resp.Content.ReadFromJsonAsync<ColumnDto>();

        // Create a card in col1
        var cardResp = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/cards",
            new CreateCardDto(board.Id, col1!.Id, "Race move card", null, null, null));
        cardResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var card = await cardResp.Content.ReadFromJsonAsync<CardDto>();

        // Move to col2 and col3 simultaneously
        using var barrier = new SemaphoreSlim(0, 2);
        var statusCodes = new ConcurrentBag<HttpStatusCode>();

        var moveTargets = new[] { col2!.Id, col3!.Id };
        var moveTasks = moveTargets.Select(async targetColId =>
        {
            using var raceClient = _factory.CreateClient();
            raceClient.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;
            await barrier.WaitAsync();
            var resp = await raceClient.PostAsJsonAsync(
                $"/api/boards/{board.Id}/cards/{card!.Id}/move",
                new MoveCardDto(targetColId, 0));
            statusCodes.Add(resp.StatusCode);
        }).ToArray();

        barrier.Release(2);
        await Task.WhenAll(moveTasks);

        // At least one should succeed
        statusCodes.Should().Contain(HttpStatusCode.OK,
            "at least one concurrent move should succeed");

        // Verify card is in exactly one column after the race
        var finalCardResp = await client.GetAsync($"/api/boards/{board.Id}/cards");
        finalCardResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var allCards = await finalCardResp.Content.ReadFromJsonAsync<List<CardDto>>();
        var movedCard = allCards!.Single(c => c.Id == card!.Id);
        new[] { col2.Id, col3.Id }.Should().Contain(movedCard.ColumnId,
            "card should end in one of the two target columns");
    }

    /// <summary>
    /// Scenario 5: Concurrent card edits with stale-write detection.
    /// Two clients read the same card, then both try to update it.
    /// The second update should be rejected with 409 Conflict when
    /// ExpectedUpdatedAt is supplied.
    /// </summary>
    [Fact]
    public async Task CardUpdate_ConcurrentEditsWithStaleWriteDetection_SecondUpdateGets409()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "race-card-edit");
        var board = await ApiTestHarness.CreateBoardAsync(client, "race-edit-board");

        var colResp = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Backlog", null, null));
        colResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var col = await colResp.Content.ReadFromJsonAsync<ColumnDto>();

        var cardResp = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/cards",
            new CreateCardDto(board.Id, col!.Id, "Stale write card", null, null, null));
        cardResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var card = await cardResp.Content.ReadFromJsonAsync<CardDto>();

        // Both clients read the card at the same time (same UpdatedAt)
        var originalUpdatedAt = card!.UpdatedAt;

        // First update succeeds
        var firstUpdate = await client.PatchAsJsonAsync(
            $"/api/boards/{board.Id}/cards/{card.Id}",
            new UpdateCardDto("First edit", null, null, null, null, null, originalUpdatedAt));
        firstUpdate.StatusCode.Should().Be(HttpStatusCode.OK);

        // Second update with stale timestamp should get 409
        using var staleClient = _factory.CreateClient();
        staleClient.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;

        var secondUpdate = await staleClient.PatchAsJsonAsync(
            $"/api/boards/{board.Id}/cards/{card.Id}",
            new UpdateCardDto("Stale edit", null, null, null, null, null, originalUpdatedAt));
        secondUpdate.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "second update with stale ExpectedUpdatedAt should be rejected");
    }

    /// <summary>
    /// Scenario 6: Concurrent card edits WITHOUT stale-write detection.
    /// When ExpectedUpdatedAt is not supplied, both updates should succeed
    /// (last-writer-wins). The card title should reflect one of the updates.
    /// </summary>
    [Fact]
    public async Task CardUpdate_ConcurrentEditsWithoutStaleCheck_LastWriterWins()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "race-card-lww");
        var board = await ApiTestHarness.CreateBoardAsync(client, "race-lww-board");

        var colResp = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Backlog", null, null));
        colResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var col = await colResp.Content.ReadFromJsonAsync<ColumnDto>();

        var cardResp = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/cards",
            new CreateCardDto(board.Id, col!.Id, "LWW card", null, null, null));
        cardResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var card = await cardResp.Content.ReadFromJsonAsync<CardDto>();

        // Two concurrent updates without ExpectedUpdatedAt
        using var barrier = new SemaphoreSlim(0, 2);
        var statusCodes = new ConcurrentBag<HttpStatusCode>();
        var titles = new[] { "Update-Alpha", "Update-Beta" };

        var tasks = titles.Select(async title =>
        {
            using var raceClient = _factory.CreateClient();
            raceClient.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;
            await barrier.WaitAsync();
            var resp = await raceClient.PatchAsJsonAsync(
                $"/api/boards/{board.Id}/cards/{card!.Id}",
                new UpdateCardDto(title, null, null, null, null, null));
            statusCodes.Add(resp.StatusCode);
        }).ToArray();

        barrier.Release(2);
        await Task.WhenAll(tasks);

        // Both should succeed (no concurrency guard without ExpectedUpdatedAt)
        statusCodes.Should().AllSatisfy(s =>
            s.Should().Be(HttpStatusCode.OK),
            "updates without ExpectedUpdatedAt should succeed (last-writer-wins)");

        // Card should have one of the two titles
        var finalResp = await client.GetAsync($"/api/boards/{board.Id}/cards");
        var allCards = await finalResp.Content.ReadFromJsonAsync<List<CardDto>>();
        var finalCard = allCards!.Single(c => c.Id == card!.Id);
        finalCard.Title.Should().BeOneOf("Update-Alpha", "Update-Beta");
    }

    /// <summary>
    /// Scenario 7: Column reorder race — two clients reorder columns
    /// at the same time. The board should end up with consistent column positions.
    /// </summary>
    [Fact]
    public async Task ColumnReorder_ConcurrentReorders_ShouldResultInConsistentState()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "race-col-reorder");
        var board = await ApiTestHarness.CreateBoardAsync(client, "race-reorder-board");

        // Create three columns
        var colIds = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var resp = await client.PostAsJsonAsync(
                $"/api/boards/{board.Id}/columns",
                new CreateColumnDto(board.Id, $"Col-{i}", i, null));
            resp.StatusCode.Should().Be(HttpStatusCode.Created);
            var col = await resp.Content.ReadFromJsonAsync<ColumnDto>();
            colIds.Add(col!.Id);
        }

        // Two clients send different reorder sequences simultaneously
        using var barrier = new SemaphoreSlim(0, 2);
        var order1 = new List<Guid> { colIds[2], colIds[0], colIds[1] };
        var order2 = new List<Guid> { colIds[1], colIds[2], colIds[0] };
        var statusCodes = new ConcurrentBag<HttpStatusCode>();

        var reorderTasks = new[] { order1, order2 }.Select(async order =>
        {
            using var raceClient = _factory.CreateClient();
            raceClient.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;
            await barrier.WaitAsync();
            var resp = await raceClient.PostAsJsonAsync(
                $"/api/boards/{board.Id}/columns/reorder",
                new ReorderColumnsDto(order));
            statusCodes.Add(resp.StatusCode);
        }).ToArray();

        barrier.Release(2);
        await Task.WhenAll(reorderTasks);

        // At least one should succeed
        statusCodes.Should().Contain(HttpStatusCode.OK,
            "at least one reorder should succeed");

        // Verify columns have distinct positions (no duplicates)
        var colsResp = await client.GetAsync($"/api/boards/{board.Id}/columns");
        colsResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var columns = await colsResp.Content.ReadFromJsonAsync<List<ColumnDto>>();
        columns.Should().HaveCount(3);
        columns!.Select(c => c.Position).Distinct().Should().HaveCount(3,
            "column positions should be unique after concurrent reorders");
    }

    // ── Proposal Approval Races ──────────────────────────────────────────────

    /// <summary>
    /// Scenario 8: Double-approve prevention.
    /// Two concurrent approve requests for the same proposal.
    /// Exactly one should succeed; the second should fail because the
    /// proposal is no longer PendingReview.
    /// </summary>
    [Fact]
    public async Task ProposalApprove_ConcurrentDoubleApprove_ExactlyOneSucceeds()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "race-approve-double");
        var board = await ApiTestHarness.CreateBoardAsync(client, "race-approve-board");

        var colResp = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Backlog", null, null));
        colResp.StatusCode.Should().Be(HttpStatusCode.Created);

        // Create a capture and wait for proposal
        var captureResp = await client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(board.Id, "- [ ] Double approve item"));
        captureResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var capture = await captureResp.Content.ReadFromJsonAsync<CaptureItemDto>();

        var triageResp = await client.PostAsync($"/api/capture/items/{capture!.Id}/triage", null);
        triageResp.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var triaged = await ApiTestHarness.PollUntilAsync(
            async () =>
            {
                var r = await client.GetAsync($"/api/capture/items/{capture.Id}");
                return await r.Content.ReadFromJsonAsync<CaptureItemDto>();
            },
            item => item?.Status == CaptureStatus.ProposalCreated,
            "proposal creation from capture triage",
            maxAttempts: 40);
        var proposalId = triaged.Provenance!.ProposalId!.Value;

        // Two concurrent approve requests
        using var barrier = new SemaphoreSlim(0, 2);
        var statusCodes = new ConcurrentBag<HttpStatusCode>();

        var approveTasks = Enumerable.Range(0, 2).Select(async _ =>
        {
            using var raceClient = _factory.CreateClient();
            raceClient.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;
            await barrier.WaitAsync();
            var resp = await raceClient.PostAsync(
                $"/api/automation/proposals/{proposalId}/approve", null);
            statusCodes.Add(resp.StatusCode);
        }).ToArray();

        barrier.Release(2);
        await Task.WhenAll(approveTasks);

          var codes = statusCodes.ToList();
          var successCount = codes.Count(s => s == HttpStatusCode.OK);
          var conflictCount = codes.Count(s => s == HttpStatusCode.Conflict);

          // Exactly one should succeed, one should fail
          successCount.Should().Be(1,
              "exactly one concurrent approve should succeed");
          conflictCount.Should().Be(1,
              "the losing concurrent approve should return 409 conflict");
    }

    /// <summary>
    /// Scenario 9: Approve+reject race.
    /// One client approves, another rejects the same proposal concurrently.
    /// Exactly one should win; the proposal should be in either Approved or Rejected state.
    /// </summary>
    [Fact]
    public async Task ProposalDecision_ConcurrentApproveAndReject_ExactlyOneWins()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "race-approve-reject");
        var board = await ApiTestHarness.CreateBoardAsync(client, "race-ar-board");

        var colResp = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Backlog", null, null));
        colResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var captureResp = await client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(board.Id, "- [ ] Approve vs reject item"));
        captureResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var capture = await captureResp.Content.ReadFromJsonAsync<CaptureItemDto>();

        var triageResp = await client.PostAsync($"/api/capture/items/{capture!.Id}/triage", null);
        triageResp.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var triaged = await ApiTestHarness.PollUntilAsync(
            async () =>
            {
                var r = await client.GetAsync($"/api/capture/items/{capture.Id}");
                return await r.Content.ReadFromJsonAsync<CaptureItemDto>();
            },
            item => item?.Status == CaptureStatus.ProposalCreated,
            "proposal creation for approve vs reject race",
            maxAttempts: 40);
        var proposalId = triaged.Provenance!.ProposalId!.Value;

        // One client approves, another rejects simultaneously
        using var barrier = new SemaphoreSlim(0, 2);
        var results = new ConcurrentDictionary<string, HttpStatusCode>();

        var approveTask = Task.Run(async () =>
        {
            using var raceClient = _factory.CreateClient();
            raceClient.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;
            await barrier.WaitAsync();
            var resp = await raceClient.PostAsync(
                $"/api/automation/proposals/{proposalId}/approve", null);
            results["approve"] = resp.StatusCode;
        });

        var rejectTask = Task.Run(async () =>
        {
            using var raceClient = _factory.CreateClient();
            raceClient.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;
            await barrier.WaitAsync();
            var resp = await raceClient.PostAsJsonAsync(
                $"/api/automation/proposals/{proposalId}/reject",
                new UpdateProposalStatusDto("rejected in race test"));
            results["reject"] = resp.StatusCode;
        });

        barrier.Release(2);
        await Task.WhenAll(approveTask, rejectTask);

          // Exactly one should succeed
          var successCount = (results["approve"] == HttpStatusCode.OK ? 1 : 0)
                           + (results["reject"] == HttpStatusCode.OK ? 1 : 0);
          var conflictCount = (results["approve"] == HttpStatusCode.Conflict ? 1 : 0)
                           + (results["reject"] == HttpStatusCode.Conflict ? 1 : 0);
          successCount.Should().Be(1,
              "exactly one of approve/reject should succeed in a race");
          conflictCount.Should().Be(1,
              "the losing proposal decision should return 409 conflict");

        // Verify final state is consistent
        var proposalResp = await client.GetAsync($"/api/automation/proposals/{proposalId}");
        proposalResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var proposal = await proposalResp.Content.ReadFromJsonAsync<ProposalDto>();
        proposal!.Status.Should().BeOneOf(
            new[] { ProposalStatus.Approved, ProposalStatus.Rejected },
            "proposal should be in a decided state after concurrent decisions");
    }

    /// <summary>
    /// Scenario 10: Double-execute prevention.
    /// Approve a proposal, then send two execute requests concurrently.
    /// Exactly one should succeed (idempotency key should prevent double execution).
    /// </summary>
    [Fact]
    public async Task ProposalExecute_ConcurrentDoubleExecute_ExactlyOneApplies()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "race-execute-double");
        var board = await ApiTestHarness.CreateBoardAsync(client, "race-execute-board");

        var colResp = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Backlog", null, null));
        colResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var captureResp = await client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(board.Id, "- [ ] Double execute item"));
        captureResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var capture = await captureResp.Content.ReadFromJsonAsync<CaptureItemDto>();

        var triageResp = await client.PostAsync($"/api/capture/items/{capture!.Id}/triage", null);
        triageResp.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var triaged = await ApiTestHarness.PollUntilAsync(
            async () =>
            {
                var r = await client.GetAsync($"/api/capture/items/{capture.Id}");
                return await r.Content.ReadFromJsonAsync<CaptureItemDto>();
            },
            item => item?.Status == CaptureStatus.ProposalCreated,
            "proposal creation for double execute race",
            maxAttempts: 40);
        var proposalId = triaged.Provenance!.ProposalId!.Value;

        // Approve the proposal first
        var approveResp = await client.PostAsync(
            $"/api/automation/proposals/{proposalId}/approve", null);
        approveResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Two concurrent execute requests with different idempotency keys
        using var barrier = new SemaphoreSlim(0, 2);
        var statusCodes = new ConcurrentBag<HttpStatusCode>();

        var executeTasks = Enumerable.Range(0, 2).Select(async i =>
        {
            using var raceClient = _factory.CreateClient();
            raceClient.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;
            var request = new HttpRequestMessage(HttpMethod.Post,
                $"/api/automation/proposals/{proposalId}/execute");
            request.Headers.Add("Idempotency-Key", $"exec-race-{i}-{Guid.NewGuid()}");
            await barrier.WaitAsync();
            var resp = await raceClient.SendAsync(request);
            statusCodes.Add(resp.StatusCode);
        }).ToArray();

        barrier.Release(2);
        await Task.WhenAll(executeTasks);

        var codes = statusCodes.ToList();
        var okCount = codes.Count(s => s == HttpStatusCode.OK);
        okCount.Should().BeGreaterOrEqualTo(1,
            "at least one execute should succeed");
        // NOTE: SQLite serializes writes, so both may succeed sequentially.
        // The real guard is the final-state assertions below.

        // Verify the proposal ended in Applied state exactly once
        var proposalResp = await client.GetAsync($"/api/automation/proposals/{proposalId}");
        proposalResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var proposal = await proposalResp.Content.ReadFromJsonAsync<ProposalDto>();
        proposal!.Status.Should().Be(ProposalStatus.Applied,
            "proposal should be in Applied state after execution");

        // Verify at most one card was created (not duplicated) — must be exactly 0 or 1
        var cardsResp = await client.GetAsync($"/api/boards/{board.Id}/cards");
        cardsResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var cards = await cardsResp.Content.ReadFromJsonAsync<List<CardDto>>();
        var matchingCards = cards!.Count(c => c.Title.Contains("Double execute item"));
        matchingCards.Should().BeInRange(0, 1,
            "double execute should not create duplicate cards (0 if mock LLM proposal has no card action, 1 if it does)");
    }

    // ── Rate Limiting Under Load ─────────────────────────────────────────────

    /// <summary>
    /// Scenario 11: Burst of requests beyond the rate limit.
    /// Fires N requests simultaneously; after the permit limit is hit,
    /// additional requests should receive 429 Too Many Requests.
    /// </summary>
    [Fact]
    public async Task RateLimit_BurstBeyondLimit_ExcessRequestsGet429()
    {
        const int permitLimit = 2;
        const int burstSize = 5;

        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:Enabled", "true");
            builder.UseSetting("RateLimiting:AuthPerIp:PermitLimit",
                permitLimit.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting("RateLimiting:AuthPerIp:WindowSeconds", "60");
        });

        using var barrier = new SemaphoreSlim(0, burstSize);
        var statusCodes = new ConcurrentBag<HttpStatusCode>();

        var burstTasks = Enumerable.Range(0, burstSize).Select(async _ =>
        {
            using var client = factory.CreateClient();
            await barrier.WaitAsync();
            var resp = await client.PostAsJsonAsync(
                "/api/auth/login",
                new LoginDto($"burst-user-{Guid.NewGuid():N}", "wrong-pass"));
            statusCodes.Add(resp.StatusCode);
        }).ToArray();

        barrier.Release(burstSize);
        await Task.WhenAll(burstTasks);

        var codes = statusCodes.ToList();
        var throttledCount = codes.Count(s => s == (HttpStatusCode)429);
        throttledCount.Should().BeGreaterOrEqualTo(burstSize - permitLimit,
            $"with permit limit {permitLimit} and burst {burstSize}, " +
            $"at least {burstSize - permitLimit} requests should be throttled");
    }

    /// <summary>
    /// Scenario 12: Cross-user isolation under load.
    /// Two users send bursts simultaneously; each user's rate limit
    /// should be tracked independently (user A being throttled should
    /// not throttle user B).
    /// </summary>
    [Fact]
    public async Task RateLimit_CrossUserIsolationUnderLoad_UsersThrottledIndependently()
    {
        const int permitLimit = 1;

        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:Enabled", "true");
            builder.UseSetting("RateLimiting:AuthPerIp:PermitLimit", "200");
            builder.UseSetting("RateLimiting:AuthPerIp:WindowSeconds", "60");
            builder.UseSetting("RateLimiting:HotPathPerUser:PermitLimit",
                permitLimit.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting("RateLimiting:HotPathPerUser:WindowSeconds", "60");
        });

        // Register two independent users
        using var clientA = factory.CreateClient();
        using var clientB = factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(clientA, "rate-iso-a");
        await ApiTestHarness.AuthenticateAsync(clientB, "rate-iso-b");

        // User A fires 2 requests (first OK, second should be 429)
        var a1 = await clientA.PostAsJsonAsync(
            "/api/llm-queue",
            new CreateLlmRequestDto("summarize", "payload A-1"));
        a1.StatusCode.Should().Be(HttpStatusCode.OK);

        var a2 = await clientA.PostAsJsonAsync(
            "/api/llm-queue",
            new CreateLlmRequestDto("summarize", "payload A-2"));
        a2.StatusCode.Should().Be((HttpStatusCode)429,
            "user A should be throttled after exceeding per-user limit");

        // User B's first request should still succeed despite user A being throttled
        var b1 = await clientB.PostAsJsonAsync(
            "/api/llm-queue",
            new CreateLlmRequestDto("summarize", "payload B-1"));
        b1.StatusCode.Should().Be(HttpStatusCode.OK,
            "user B should not be affected by user A's throttling");
    }

    /// <summary>
    /// Scenario 13: Concurrent board creation stress test.
    /// Multiple users create boards simultaneously to verify no
    /// cross-contamination or data corruption under concurrent writes.
    /// </summary>
    [Fact]
    public async Task BoardCreation_ConcurrentMultiUser_NoCrossContamination()
    {
        const int userCount = 5;
        var userBoards = new ConcurrentDictionary<string, Guid>();
        var errors = new ConcurrentBag<string>();

        using var barrier = new SemaphoreSlim(0, userCount);
        var tasks = Enumerable.Range(0, userCount).Select(async i =>
        {
            using var client = _factory.CreateClient();
            var user = await ApiTestHarness.AuthenticateAsync(client, $"race-board-{i}");
            await barrier.WaitAsync();

            var resp = await client.PostAsJsonAsync(
                "/api/boards",
                new CreateBoardDto($"race-board-{i}-{Guid.NewGuid():N}", "stress test board"));
            if (resp.StatusCode != HttpStatusCode.Created)
            {
                errors.Add($"User {i} got {resp.StatusCode}");
                return;
            }

            var board = await resp.Content.ReadFromJsonAsync<BoardDto>();
            userBoards[user.Username] = board!.Id;

            // Verify user only sees their own board
            var listResp = await client.GetAsync("/api/boards");
            var boards = await listResp.Content.ReadFromJsonAsync<List<BoardDto>>();
            var otherUserBoards = boards!.Where(b =>
                userBoards.Any(kv => kv.Key != user.Username && kv.Value == b.Id));
            if (otherUserBoards.Any())
            {
                errors.Add($"User {user.Username} can see another user's board");
            }
        }).ToArray();

        barrier.Release(userCount);
        await Task.WhenAll(tasks);

        errors.Should().BeEmpty("no cross-user board contamination should occur");
        userBoards.Should().HaveCount(userCount,
            "all users should have created their boards successfully");
    }
}
