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
        var userBoards = new ConcurrentDictionary<string, Guid>();
        var errors = new ConcurrentBag<string>();

        using var barrier = new SemaphoreSlim(0, userCount);
        var tasks = Enumerable.Range(0, userCount).Select(async i =>
        {
            using var client = _factory.CreateClient();
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
                    if (resp.StatusCode != HttpStatusCode.Created)
                    {
                        errors.Add(
                            $"User {ctx.User.Username} item {j} got {resp.StatusCode}");
                    }
                })).ToArray();

            barrier.Release(userCount * itemsPerUser);
            await Task.WhenAll(allTasks);

            errors.Should().BeEmpty("all concurrent capture item creations should succeed");

            // Verify each user only sees their own capture items
            foreach (var ctx in userContexts)
            {
                var captureResp = await ctx.Client.GetAsync(
                    $"/api/capture/items?boardId={ctx.Board.Id}");
                captureResp.StatusCode.Should().Be(HttpStatusCode.OK);
                var items = await captureResp.Content
                    .ReadFromJsonAsync<List<CaptureItemDto>>();

                items.Should().NotBeNull();
                items!.Should().HaveCount(itemsPerUser,
                    $"user {ctx.User.Username} should see exactly " +
                    $"{itemsPerUser} capture items");

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
