using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Api.Tests.Concurrency;

/// <summary>
/// Queue claim race condition tests exercising:
/// 1. Double-claim prevention with 10 parallel workers on same LLM queue item
/// 2. Capture triage claim with stale expectedUpdatedAt
/// 3. Batch processing with concurrent workers (no item processed twice)
///
/// Uses Task.WhenAll with SemaphoreSlim barriers for truly simultaneous execution.
///
/// NOTE: SQLite uses file-level write locking, which serializes concurrent writes
/// at the database level. These tests validate application-layer claim guards
/// (optimistic concurrency via UpdatedAt, status checks) regardless of whether
/// SQLite serializes the underlying writes. In production with PostgreSQL, these
/// guards would prevent true concurrent claim races at the row level.
///
/// See GitHub issue #705 (TST-55).
///
/// Worker split (issue #1335): this class keeps the base worker-RUNNING factory because its
/// capture-triage tests DEPEND on the live triage drain (they poll until the worker turns the
/// triaged capture into a proposal). The process-next claim races — which the live
/// <c>LlmQueueToProposalWorker</c> can pre-empt by draining the seeded Pending row first — moved
/// to <see cref="ProcessNextClaimRaceTests"/> on the workerless factory.
/// </summary>
public class QueueClaimRaceTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public QueueClaimRaceTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Scenario 2: Capture triage with stale timestamp.
    /// After a capture item has been triaged once, a second triage attempt
    /// (simulating a stale read) should not produce a duplicate proposal.
    /// </summary>
    [Fact]
    public async Task CaptureTriage_StaleTimestamp_NoDuplicateProposal()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "queue-stale-claim");
        var board = await ApiTestHarness.CreateBoardAsync(client, "queue-stale-board");

        var colResp = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Backlog", null, null));
        colResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var captureResp = await client.PostAsJsonAsync(
            "/api/capture/items",
            new CreateCaptureItemDto(board.Id, "- [ ] Stale claim item"));
        captureResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var capture = await captureResp.Content.ReadFromJsonAsync<CaptureItemDto>();
        capture.Should().NotBeNull();

        // First triage
        var firstTriage = await client.PostAsync($"/api/capture/items/{capture!.Id}/triage", null);
        firstTriage.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Wait for processing to advance
        await ApiTestHarness.PollUntilAsync(
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

        // Should either reject or be idempotent
        secondTriage.StatusCode.Should().BeOneOf(
            HttpStatusCode.Accepted,
            HttpStatusCode.OK,
            HttpStatusCode.Conflict,
            HttpStatusCode.BadRequest,
            (HttpStatusCode)429);

        // Count proposals to ensure no duplicates
        var proposalsResp = await client.GetAsync($"/api/automation/proposals?boardId={board.Id}");
        proposalsResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var proposals = await proposalsResp.Content.ReadFromJsonAsync<List<ProposalDto>>();
        proposals.Should().NotBeNull();

        var captureProposals = proposals!.Where(p =>
            p.SourceReferenceId == capture.Id.ToString()).ToList();
        captureProposals.Should().HaveCountLessOrEqualTo(1,
            "stale re-triage should not create duplicate proposals");
    }

    /// <summary>
    /// Scenario 3: Batch processing with concurrent workers on different items.
    /// Multiple capture items are triaged simultaneously by different workers.
    /// No item should be processed twice.
    /// </summary>
    [Fact]
    public async Task CaptureTriage_BatchConcurrentWorkers_NoItemProcessedTwice()
    {
        const int batchSize = 5;
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "queue-batch-workers");
        var board = await ApiTestHarness.CreateBoardAsync(client, "queue-batch-board");

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
            using var workerClient = _factory.CreateClient();
            workerClient.DefaultRequestHeaders.Authorization =
                client.DefaultRequestHeaders.Authorization;
            await barrier.WaitAsync();
            var resp = await workerClient.PostAsync(
                $"/api/capture/items/{captureId}/triage", null);
            results[captureId] = resp.StatusCode;
        }).ToArray();

        barrier.Release(batchSize);
        await Task.WhenAll(tasks);

        // Each distinct item should triage without conflict
        results.Values.Should().AllSatisfy(s =>
            s.Should().BeOneOf(HttpStatusCode.Accepted, HttpStatusCode.OK),
            "each distinct capture item should triage without conflict");

        // Poll for proposals to settle, then verify no duplicates.
        var proposals = await ApiTestHarness.PollUntilAsync(
            async () =>
            {
                var proposalsResp = await client.GetAsync($"/api/automation/proposals?boardId={board.Id}");
                proposalsResp.StatusCode.Should().Be(HttpStatusCode.OK);
                return await proposalsResp.Content.ReadFromJsonAsync<List<ProposalDto>>()
                       ?? new List<ProposalDto>();
            },
            p => p.Count >= captureIds.Count,
            "batch triage proposals to settle",
            maxAttempts: 60,
            interval: TimeSpan.FromMilliseconds(500));

        // Each capture item should have exactly one proposal (no duplicates, no data loss)
        foreach (var captureId in captureIds)
        {
            var matching = proposals!.Count(p => p.SourceReferenceId == captureId.ToString());
            matching.Should().Be(1,
                $"capture item {captureId} should have exactly one proposal (no duplicate processing, no data loss)");
        }
    }

}
