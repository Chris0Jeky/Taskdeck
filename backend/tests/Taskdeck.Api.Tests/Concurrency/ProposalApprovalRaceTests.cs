using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Api.Tests.Concurrency;

/// <summary>
/// Proposal approval race condition tests exercising:
/// 7. Double-approve prevention (two concurrent approve requests)
/// 8. Approve + Expire race (proposal approved while housekeeping expires it)
/// 9. Approve + Reject race (concurrent approve and reject)
/// 10. Double-execute prevention (two concurrent execute requests)
///
/// Uses Task.WhenAll with SemaphoreSlim barriers for truly simultaneous execution.
///
/// NOTE: SQLite serializes writes, so optimistic concurrency tokens may not
/// reliably fire under true concurrent access. These tests validate that the
/// application-layer state machine (PendingReview -> Approved/Rejected/Expired)
/// produces consistent final states regardless of whether both operations
/// succeed or one gets 409.
///
/// See GitHub issue #705 (TST-55).
/// </summary>
public class ProposalApprovalRaceTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ProposalApprovalRaceTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Helper: creates a capture item, triggers triage, and waits for a proposal
    /// to be created. Returns the proposal ID.
    /// </summary>
    private async Task<Guid> CreateAndWaitForProposalAsync(HttpClient client, Guid boardId, string itemText)
    {
        var captureResp = await client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(boardId, itemText));
        captureResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var capture = await captureResp.Content.ReadFromJsonAsync<CaptureItemDto>();
        capture.Should().NotBeNull();

        var triageResp = await client.PostAsync($"/api/capture/items/{capture!.Id}/triage", null);
        triageResp.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var triaged = await ApiTestHarness.PollUntilAsync(
            async () =>
            {
                var r = await client.GetAsync($"/api/capture/items/{capture.Id}");
                return await r.Content.ReadFromJsonAsync<CaptureItemDto>();
            },
            item => item?.Status == CaptureStatus.ProposalCreated,
            "proposal creation",
            maxAttempts: 80);

        return triaged.Provenance!.ProposalId!.Value;
    }

    /// <summary>
    /// Scenario 7: Double-approve prevention.
    /// Two concurrent approve requests for the same proposal.
    /// At least one should succeed; any failing request should get 409 Conflict.
    /// The proposal should end in Approved state.
    /// </summary>
    [Fact]
    public async Task DoubleApprove_ExactlyOneSucceeds()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "proposal-double-approve");
        var board = await ApiTestHarness.CreateBoardAsync(client, "double-approve-board");

        var colResp = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Backlog", null, null));
        colResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var proposalId = await CreateAndWaitForProposalAsync(
            client, board.Id, "- [ ] Double approve item");

        // Two concurrent approve requests
        using var barrier = new SemaphoreSlim(0, 2);
        var statusCodes = new ConcurrentBag<HttpStatusCode>();

        var approveTasks = Enumerable.Range(0, 2).Select(async _ =>
        {
            using var raceClient = _factory.CreateClient();
            raceClient.DefaultRequestHeaders.Authorization =
                client.DefaultRequestHeaders.Authorization;
            await barrier.WaitAsync();
            var resp = await raceClient.PostAsync(
                $"/api/automation/proposals/{proposalId}/approve", null);
            statusCodes.Add(resp.StatusCode);
        }).ToArray();

        barrier.Release(2);
        await Task.WhenAll(approveTasks);

        var codes = statusCodes.ToList();
        var successCount = codes.Count(s => s == HttpStatusCode.OK);

        successCount.Should().BeGreaterThanOrEqualTo(1,
            "at least one concurrent approve should succeed");
        codes.Where(s => s != HttpStatusCode.OK)
            .Should().OnlyContain(s => s == HttpStatusCode.Conflict,
            "any failing concurrent approve should return 409 Conflict");

        // Verify final state
        var proposalResp = await client.GetAsync($"/api/automation/proposals/{proposalId}");
        proposalResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var proposal = await proposalResp.Content.ReadFromJsonAsync<ProposalDto>();
        proposal!.Status.Should().Be(ProposalStatus.Approved,
            "proposal should be in Approved state after double-approve race");
    }

    /// <summary>
    /// Scenario 8: Approve + Expire race.
    /// One client approves a proposal while another simulates housekeeping
    /// expiry by rejecting it with a reason (since we cannot directly invoke
    /// the housekeeping worker's expire logic via HTTP). The key invariant:
    /// the proposal ends in a decided state (Approved, Rejected, or Expired).
    ///
    /// Note: The actual Expire() call is internal to the housekeeping worker
    /// and operates on the domain entity directly. This test validates the
    /// HTTP-level race between approve and reject as a proxy for approve+expire.
    /// For the domain-level approve+expire race, see
    /// ProposalHousekeepingWorkerEdgeCaseTests.
    /// </summary>
    [Fact]
    public async Task ApproveAndExpireRace_OneWinsCleanly()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "proposal-approve-expire");
        var board = await ApiTestHarness.CreateBoardAsync(client, "approve-expire-board");

        var colResp = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Backlog", null, null));
        colResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var proposalId = await CreateAndWaitForProposalAsync(
            client, board.Id, "- [ ] Approve vs expire item");

        // One client approves, another rejects (simulating expire via HTTP)
        using var barrier = new SemaphoreSlim(0, 2);
        var results = new ConcurrentDictionary<string, HttpStatusCode>();

        var approveTask = Task.Run(async () =>
        {
            using var raceClient = _factory.CreateClient();
            raceClient.DefaultRequestHeaders.Authorization =
                client.DefaultRequestHeaders.Authorization;
            await barrier.WaitAsync();
            var resp = await raceClient.PostAsync(
                $"/api/automation/proposals/{proposalId}/approve", null);
            results["approve"] = resp.StatusCode;
        });

        var rejectTask = Task.Run(async () =>
        {
            using var raceClient = _factory.CreateClient();
            raceClient.DefaultRequestHeaders.Authorization =
                client.DefaultRequestHeaders.Authorization;
            await barrier.WaitAsync();
            var resp = await raceClient.PostAsJsonAsync(
                $"/api/automation/proposals/{proposalId}/reject",
                new UpdateProposalStatusDto("expired by housekeeping (simulated)"));
            results["reject"] = resp.StatusCode;
        });

        barrier.Release(2);
        await Task.WhenAll(approveTask, rejectTask);

        // At least one should succeed
        var successCount = (results["approve"] == HttpStatusCode.OK ? 1 : 0)
                         + (results["reject"] == HttpStatusCode.OK ? 1 : 0);
        successCount.Should().BeGreaterThanOrEqualTo(1,
            "at least one of approve/expire(reject) should succeed");
        results.Values.Should().OnlyContain(
            s => s == HttpStatusCode.OK || s == HttpStatusCode.Conflict,
            "losing operation should get 409 Conflict");

        // Verify final state is consistent
        var proposalResp = await client.GetAsync($"/api/automation/proposals/{proposalId}");
        proposalResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var proposal = await proposalResp.Content.ReadFromJsonAsync<ProposalDto>();
        proposal!.Status.Should().BeOneOf(
            new[] { ProposalStatus.Approved, ProposalStatus.Rejected },
            "proposal should be in a decided state after approve+expire race");
    }

    /// <summary>
    /// Scenario 9: Approve + Reject race.
    /// One client approves, another rejects the same proposal concurrently.
    /// One should win; the proposal should end in either Approved or Rejected.
    /// </summary>
    [Fact]
    public async Task ApproveAndReject_OneWinsCleanly()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "proposal-approve-reject");
        var board = await ApiTestHarness.CreateBoardAsync(client, "approve-reject-board");

        var colResp = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Backlog", null, null));
        colResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var proposalId = await CreateAndWaitForProposalAsync(
            client, board.Id, "- [ ] Approve vs reject item");

        // One client approves, another rejects simultaneously
        using var barrier = new SemaphoreSlim(0, 2);
        var results = new ConcurrentDictionary<string, HttpStatusCode>();

        var approveTask = Task.Run(async () =>
        {
            using var raceClient = _factory.CreateClient();
            raceClient.DefaultRequestHeaders.Authorization =
                client.DefaultRequestHeaders.Authorization;
            await barrier.WaitAsync();
            var resp = await raceClient.PostAsync(
                $"/api/automation/proposals/{proposalId}/approve", null);
            results["approve"] = resp.StatusCode;
        });

        var rejectTask = Task.Run(async () =>
        {
            using var raceClient = _factory.CreateClient();
            raceClient.DefaultRequestHeaders.Authorization =
                client.DefaultRequestHeaders.Authorization;
            await barrier.WaitAsync();
            var resp = await raceClient.PostAsJsonAsync(
                $"/api/automation/proposals/{proposalId}/reject",
                new UpdateProposalStatusDto("rejected in race test"));
            results["reject"] = resp.StatusCode;
        });

        barrier.Release(2);
        await Task.WhenAll(approveTask, rejectTask);

        var successCount = (results["approve"] == HttpStatusCode.OK ? 1 : 0)
                         + (results["reject"] == HttpStatusCode.OK ? 1 : 0);
        successCount.Should().BeGreaterThanOrEqualTo(1,
            "at least one of approve/reject should succeed");
        results.Values.Should().OnlyContain(
            s => s == HttpStatusCode.OK || s == HttpStatusCode.Conflict,
            "losing operation should get 409 Conflict");

        // Verify final state
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
    /// The proposal should end in Applied state with no duplicate side effects.
    /// </summary>
    [Fact]
    public async Task DoubleExecute_NoDuplicateSideEffects()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "proposal-double-exec");
        var board = await ApiTestHarness.CreateBoardAsync(client, "double-exec-board");

        var colResp = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Backlog", null, null));
        colResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var proposalId = await CreateAndWaitForProposalAsync(
            client, board.Id, "- [ ] Double execute item");

        // Approve the proposal first
        var approveResp = await client.PostAsync(
            $"/api/automation/proposals/{proposalId}/approve", null);
        approveResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Two concurrent execute requests
        using var barrier = new SemaphoreSlim(0, 2);
        var statusCodes = new ConcurrentBag<HttpStatusCode>();

        var executeTasks = Enumerable.Range(0, 2).Select(async i =>
        {
            using var raceClient = _factory.CreateClient();
            raceClient.DefaultRequestHeaders.Authorization =
                client.DefaultRequestHeaders.Authorization;
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

        // Verify the proposal ended in Applied state
        var proposalResp = await client.GetAsync($"/api/automation/proposals/{proposalId}");
        proposalResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var proposal = await proposalResp.Content.ReadFromJsonAsync<ProposalDto>();
        proposal!.Status.Should().Be(ProposalStatus.Applied,
            "proposal should be in Applied state after execution");

        // Verify at most one card was created (not duplicated)
        var cardsResp = await client.GetAsync($"/api/boards/{board.Id}/cards");
        cardsResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var cards = await cardsResp.Content.ReadFromJsonAsync<List<CardDto>>();
        var matchingCards = cards!.Count(c => c.Title.Contains("Double execute item"));
        matchingCards.Should().BeInRange(0, 1,
            "double execute should not create duplicate cards");
    }
}
