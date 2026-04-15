using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests.Concurrency;

/// <summary>
/// Card update conflict tests exercising:
/// 4. Concurrent card moves to different columns
/// 5. Concurrent card edits with stale-write detection (ExpectedUpdatedAt)
/// 6. Column reorder race (two users reorder simultaneously)
///
/// Uses Task.WhenAll with SemaphoreSlim barriers for truly simultaneous execution.
///
/// NOTE: SQLite serializes writes at the file level. The application-layer
/// guards (optimistic concurrency via ExpectedUpdatedAt, status checks) are
/// what these tests validate. With SQLite, concurrent writes serialize, so
/// "last-writer-wins" behavior may differ from PostgreSQL row-level locking.
///
/// See GitHub issue #705 (TST-55).
/// </summary>
public class CardUpdateConflictTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public CardUpdateConflictTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Scenario 4: Two concurrent card moves to different columns.
    /// Both may succeed under SQLite serialization (last-writer-wins),
    /// but the card must end up in exactly one column.
    /// </summary>
    [Fact]
    public async Task ConcurrentMoves_ToDifferentColumns_CardEndsInExactlyOneColumn()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "card-move-race");
        var board = await ApiTestHarness.CreateBoardAsync(client, "card-move-board");

        // Create three columns
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
            new CreateCardDto(board.Id, col1!.Id, "Move race card", null, null, null));
        cardResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var card = await cardResp.Content.ReadFromJsonAsync<CardDto>();

        // Move to col2 and col3 simultaneously
        using var barrier = new SemaphoreSlim(0, 2);
        var statusCodes = new ConcurrentBag<HttpStatusCode>();

        var moveTargets = new[] { col2!.Id, col3!.Id };
        var moveTasks = moveTargets.Select(async targetColId =>
        {
            using var raceClient = _factory.CreateClient();
            raceClient.DefaultRequestHeaders.Authorization =
                client.DefaultRequestHeaders.Authorization;
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
    /// Two clients read the same card, then both try to update it using
    /// the same ExpectedUpdatedAt. The second update should be rejected
    /// with 409 Conflict.
    /// </summary>
    [Fact]
    public async Task ConcurrentEdits_WithExpectedUpdatedAt_SecondUpdateGets409()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "card-edit-stale");
        var board = await ApiTestHarness.CreateBoardAsync(client, "card-edit-board");

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
        staleClient.DefaultRequestHeaders.Authorization =
            client.DefaultRequestHeaders.Authorization;

        var secondUpdate = await staleClient.PatchAsJsonAsync(
            $"/api/boards/{board.Id}/cards/{card.Id}",
            new UpdateCardDto("Stale edit", null, null, null, null, null, originalUpdatedAt));
        secondUpdate.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "second update with stale ExpectedUpdatedAt should be rejected");
    }

    /// <summary>
    /// Scenario 5b: Concurrent card edits WITHOUT stale-write detection.
    /// When ExpectedUpdatedAt is not supplied, both updates should succeed
    /// (last-writer-wins). The card title should reflect one of the updates.
    /// </summary>
    [Fact]
    public async Task ConcurrentEdits_WithoutStaleCheck_LastWriterWins()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "card-edit-lww");
        var board = await ApiTestHarness.CreateBoardAsync(client, "card-lww-board");

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
            raceClient.DefaultRequestHeaders.Authorization =
                client.DefaultRequestHeaders.Authorization;
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
    /// Scenario 6: Column reorder race.
    /// Two clients reorder columns at the same time. The board should end up
    /// with consistent column positions (no duplicates, no gaps).
    /// </summary>
    [Fact]
    public async Task ColumnReorder_ConcurrentReorders_ConsistentFinalState()
    {
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "col-reorder-race");
        var board = await ApiTestHarness.CreateBoardAsync(client, "col-reorder-board");

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
            raceClient.DefaultRequestHeaders.Authorization =
                client.DefaultRequestHeaders.Authorization;
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

    /// <summary>
    /// Scenario 6b: Concurrent card creation in the same column.
    /// Multiple users create cards in the same column simultaneously.
    /// All cards should be created with no duplicates or losses.
    /// </summary>
    [Fact]
    public async Task ConcurrentCardCreation_SameColumn_AllCreatedNoDuplicates()
    {
        const int cardCount = 5;
        using var client = _factory.CreateClient();
        await ApiTestHarness.AuthenticateAsync(client, "card-create-race");
        var board = await ApiTestHarness.CreateBoardAsync(client, "card-create-board");

        var colResp = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Backlog", null, null));
        colResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var col = await colResp.Content.ReadFromJsonAsync<ColumnDto>();

        // Create cards concurrently
        using var barrier = new SemaphoreSlim(0, cardCount);
        var statusCodes = new ConcurrentBag<HttpStatusCode>();
        var createdIds = new ConcurrentBag<Guid>();

        var tasks = Enumerable.Range(0, cardCount).Select(async i =>
        {
            using var raceClient = _factory.CreateClient();
            raceClient.DefaultRequestHeaders.Authorization =
                client.DefaultRequestHeaders.Authorization;
            await barrier.WaitAsync();
            var resp = await raceClient.PostAsJsonAsync(
                $"/api/boards/{board.Id}/cards",
                new CreateCardDto(board.Id, col!.Id, $"Concurrent card {i}", null, null, null));
            statusCodes.Add(resp.StatusCode);
            if (resp.StatusCode == HttpStatusCode.Created)
            {
                var created = await resp.Content.ReadFromJsonAsync<CardDto>();
                if (created != null) createdIds.Add(created.Id);
            }
        }).ToArray();

        barrier.Release(cardCount);
        await Task.WhenAll(tasks);

        // All should succeed
        statusCodes.Should().AllSatisfy(s =>
            s.Should().Be(HttpStatusCode.Created),
            "all concurrent card creations should succeed");

        // All IDs should be unique
        createdIds.Distinct().Should().HaveCount(cardCount,
            "each card should have a unique ID (no duplicates)");

        // Verify via list endpoint
        var cardsResp = await client.GetAsync($"/api/boards/{board.Id}/cards");
        var allCards = await cardsResp.Content.ReadFromJsonAsync<List<CardDto>>();
        var concurrentCards = allCards!.Where(c =>
            c.Title.StartsWith("Concurrent card ")).ToList();
        concurrentCards.Should().HaveCount(cardCount,
            "all concurrently created cards should appear in the list");
    }
}
