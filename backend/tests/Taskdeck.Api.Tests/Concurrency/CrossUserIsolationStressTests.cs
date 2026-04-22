using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Xunit;

namespace Taskdeck.Api.Tests.Concurrency;

/// <summary>
/// Cross-user isolation stress tests exercising:
/// - Concurrent board creation by multiple users
/// - Verification that no cross-user data leakage occurs
///
/// Uses Task.WhenAll with SemaphoreSlim barriers for truly simultaneous execution.
///
/// See GitHub issue #705 (TST-55).
/// </summary>
public class CrossUserIsolationStressTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public CrossUserIsolationStressTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Concurrent board creation by multiple users.
    /// Each user creates a board simultaneously. No user should see
    /// another user's board (cross-user data isolation).
    /// </summary>
    [Fact]
    public async Task ConcurrentBoardCreation_NoCrossUserContamination()
    {
        const int userCount = 5;
        var userBoards = new ConcurrentDictionary<string, (Guid BoardId, HttpClient Client)>();
        var errors = new ConcurrentBag<string>();

        // Phase 1: All users create boards concurrently
        using var barrier = new SemaphoreSlim(0, userCount);
        var tasks = Enumerable.Range(0, userCount).Select(async i =>
        {
            var client = _factory.CreateClient();
            var user = await ApiTestHarness.AuthenticateAsync(client, $"isolation-{i}");
            await barrier.WaitAsync();

            var resp = await client.PostAsJsonAsync(
                "/api/boards",
                new CreateBoardDto(
                    $"isolation-board-{i}-{Guid.NewGuid():N}",
                    "stress test board"));
            if (resp.StatusCode != HttpStatusCode.Created)
            {
                errors.Add($"User {i} got {resp.StatusCode}");
                client.Dispose();
                return;
            }

            var board = await resp.Content.ReadFromJsonAsync<BoardDto>();
            userBoards[user.Username] = (board!.Id, client);
        }).ToArray();

        barrier.Release(userCount);
        await Task.WhenAll(tasks);

        try
        {
            errors.Should().BeEmpty("all users should create boards successfully");
            userBoards.Should().HaveCount(userCount,
                "all users should have created their boards successfully");

            // Phase 2: After all boards exist, verify isolation with the
            // complete set of known board IDs (no race against concurrent inserts)
            var allBoardIds = userBoards.Values.Select(v => v.BoardId).ToHashSet();
            foreach (var (username, (boardId, client)) in userBoards)
            {
                var boards = await ApiTestHarness.ListBoardsAsync(client);
                var visibleOtherBoards = boards.Where(b =>
                    allBoardIds.Contains(b.Id) && b.Id != boardId).ToList();
                visibleOtherBoards.Should().BeEmpty(
                    $"user {username} should not see other users' boards");
            }
        }
        finally
        {
            foreach (var (_, (_, client)) in userBoards)
                client.Dispose();
        }
    }

    /// <summary>
    /// Concurrent capture item creation by different users on their own boards.
    /// Each user's capture items should be isolated to their own board.
    /// </summary>
    [Fact]
    public async Task ConcurrentCaptureCreation_UserIsolation()
    {
        const int userCount = 3;
        const int itemsPerUser = 3;
        var errors = new ConcurrentBag<string>();

        // Set up users and boards sequentially (setup phase)
        var userContexts = new List<(HttpClient Client, TestUserContext User, BoardDto Board)>();
        try
        {
            for (var i = 0; i < userCount; i++)
            {
                var client = _factory.CreateClient();
                var user = await ApiTestHarness.AuthenticateAsync(client, $"cap-iso-{i}");
                var board = await ApiTestHarness.CreateBoardAsync(client, $"cap-iso-board-{i}");

                var colResp = await client.PostAsJsonAsync(
                    $"/api/boards/{board.Id}/columns",
                    new CreateColumnDto(board.Id, "Backlog", null, null));
                colResp.StatusCode.Should().Be(HttpStatusCode.Created);

                userContexts.Add((client, user, board));
            }

            // All users create capture items concurrently
            using var barrier = new SemaphoreSlim(0, userCount * itemsPerUser);
            var allTasks = userContexts.SelectMany(ctx =>
                Enumerable.Range(0, itemsPerUser).Select(async j =>
                {
                    using var raceClient = _factory.CreateClient();
                    raceClient.DefaultRequestHeaders.Authorization =
                        ctx.Client.DefaultRequestHeaders.Authorization;
                    await barrier.WaitAsync();
                    var resp = await raceClient.PostAsJsonAsync(
                        "/api/capture/items",
                        new CreateCaptureItemDto(ctx.Board.Id,
                            $"- [ ] User {ctx.User.Username} item {j}"));
                    if (resp.StatusCode != HttpStatusCode.Created
                        && resp.StatusCode != HttpStatusCode.InternalServerError)
                    {
                        errors.Add(
                            $"User {ctx.User.Username} item {j} got {resp.StatusCode}");
                    }
                })).ToArray();

            barrier.Release(userCount * itemsPerUser);
            await Task.WhenAll(allTasks);

            // Under SQLite's file-level write lock, transient 500s from DB contention
            // are a known test-environment limitation and are tolerated.
            errors.Should().BeEmpty(
                "only Created or transient 500 (SQLite contention) are acceptable");

            // Verify each user only sees their own capture items
            foreach (var ctx in userContexts)
            {
                var captureResp = await ctx.Client.GetAsync(
                    $"/api/capture/items?boardId={ctx.Board.Id}");
                captureResp.StatusCode.Should().Be(HttpStatusCode.OK);
                var items = await captureResp.Content
                    .ReadFromJsonAsync<List<CaptureItemDto>>();

                items.Should().NotBeNull();
                items!.Count.Should().BeGreaterOrEqualTo(1,
                    $"user {ctx.User.Username} should have at least 1 capture item " +
                    $"(some may be missing due to transient SQLite contention)");
                items!.Count.Should().BeLessOrEqualTo(itemsPerUser,
                    $"user {ctx.User.Username} should have at most {itemsPerUser} items");

                // Verify none of the items belong to other users
                foreach (var item in items)
                {
                    item.BoardId.Should().Be(ctx.Board.Id,
                        $"user {ctx.User.Username} should only see items " +
                        $"from their own board");
                }
            }
        }
        finally
        {
            // Dispose clients even if assertions fail
            foreach (var ctx in userContexts)
                ctx.Client.Dispose();
        }
    }
}
