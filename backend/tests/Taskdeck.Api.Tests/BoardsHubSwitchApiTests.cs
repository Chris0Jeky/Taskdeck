using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Taskdeck.Api.Realtime;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// JoinBoard must self-heal a missed LeaveBoard: when a connection joins board B while the
/// tracker still has it on board A, the hub drops it from A's SignalR group and publishes
/// A's updated roster, instead of leaving a ghost group member plus a stale presence entry.
/// </summary>
public class BoardsHubSwitchApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public BoardsHubSwitchApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SwitchWithoutLeave_StopsOldBoardMutationsAndPublishesOldSnapshot()
    {
        var ownerClient = _factory.CreateClient();
        var memberClient = _factory.CreateClient();

        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "switch-owner");
        var member = await ApiTestHarness.AuthenticateAsync(memberClient, "switch-member");
        var boardA = await ApiTestHarness.CreateBoardAsync(ownerClient, "switch-board-a");
        var boardB = await ApiTestHarness.CreateBoardAsync(ownerClient, "switch-board-b");
        await GrantEditorAsync(ownerClient, boardA.Id, member.UserId);
        await GrantEditorAsync(ownerClient, boardB.Id, member.UserId);

        await using var switcher = SignalRTestHelper.CreateBoardsHubConnection(_factory, member.Token);
        await switcher.StartAsync();
        await using var observer = SignalRTestHelper.CreateBoardsHubConnection(_factory, owner.Token);
        await observer.StartAsync();

        var observerPresence = new EventCollector<BoardPresenceSnapshot>();
        observer.On<BoardPresenceSnapshot>("boardPresence", snapshot => observerPresence.Add(snapshot));

        await observer.InvokeAsync("JoinBoard", boardA.Id);
        await switcher.InvokeAsync("JoinBoard", boardA.Id);
        await SignalRTestHelper.WaitForEventsAsync(observerPresence, 2);

        // Positive control: boardMutation reaches the switcher before the switch, so the
        // negative assertion below cannot pass vacuously.
        var preSwitchMutation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        switcher.On<object>("boardMutation", _ => preSwitchMutation.TrySetResult());
        var colResponse = await ownerClient.PostAsJsonAsync(
            $"/api/boards/{boardA.Id}/columns",
            new CreateColumnDto(boardA.Id, "SwitchCol", null, null));
        colResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var preCompleted = await Task.WhenAny(preSwitchMutation.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        preCompleted.Should().Be(preSwitchMutation.Task, "the harness must observe boardMutation before the switch");

        // The switch under test: straight to B with no LeaveBoard(A).
        observerPresence.Clear();
        await switcher.InvokeAsync("JoinBoard", boardB.Id);

        // The old board's remaining members see an updated roster without the switcher.
        var oldSnapshots = await SignalRTestHelper.WaitForEventsAsync(observerPresence, 1);
        var oldSnapshot = oldSnapshots.Should().ContainSingle().Subject;
        oldSnapshot.BoardId.Should().Be(boardA.Id);
        oldSnapshot.Members.Should().ContainSingle(m => m.UserId == owner.UserId);

        // A board-A mutation after the switch reaches the observer but not the ghost.
        var observerPostSwitch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        observer.On<object>("boardMutation", _ => observerPostSwitch.TrySetResult());
        var switcherPostSwitch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        switcher.On<object>("boardMutation", _ => switcherPostSwitch.TrySetResult());

        var col = await colResponse.Content.ReadFromJsonAsync<ColumnDto>();
        var cardResponse = await ownerClient.PostAsJsonAsync(
            $"/api/boards/{boardA.Id}/cards",
            new CreateCardDto(boardA.Id, col!.Id, "post-switch card", null, null, null));
        cardResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var observerCompleted = await Task.WhenAny(observerPostSwitch.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        observerCompleted.Should().Be(observerPostSwitch.Task, "a remaining board-A member must keep receiving board mutations");

        var switcherCompleted = await Task.WhenAny(switcherPostSwitch.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        switcherCompleted.Should().NotBe(switcherPostSwitch.Task, "a switched connection must leave the old board group");

        // Over-heal control: the new membership still works.
        var newBoardMutation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        switcher.On<object>("boardMutation", payload =>
        {
            // Only board-B traffic counts; board-A listeners above stay silent by the assertion above.
            newBoardMutation.TrySetResult();
        });
        var colBResponse = await ownerClient.PostAsJsonAsync(
            $"/api/boards/{boardB.Id}/columns",
            new CreateColumnDto(boardB.Id, "SwitchColB", null, null));
        colBResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var newBoardCompleted = await Task.WhenAny(newBoardMutation.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        newBoardCompleted.Should().Be(newBoardMutation.Task, "the switched connection must receive the new board mutations");
    }

    [Fact]
    public async Task SameBoardRejoin_DoesNotPublishSpuriousLeave()
    {
        var ownerClient = _factory.CreateClient();
        var memberClient = _factory.CreateClient();

        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "rejoin-owner");
        var member = await ApiTestHarness.AuthenticateAsync(memberClient, "rejoin-member");
        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "rejoin-board");
        await GrantEditorAsync(ownerClient, board.Id, member.UserId);

        await using var joiner = SignalRTestHelper.CreateBoardsHubConnection(_factory, member.Token);
        await joiner.StartAsync();
        await using var observer = SignalRTestHelper.CreateBoardsHubConnection(_factory, owner.Token);
        await observer.StartAsync();

        var observerPresence = new EventCollector<BoardPresenceSnapshot>();
        observer.On<BoardPresenceSnapshot>("boardPresence", snapshot => observerPresence.Add(snapshot));

        await observer.InvokeAsync("JoinBoard", board.Id);
        await joiner.InvokeAsync("JoinBoard", board.Id);
        await SignalRTestHelper.WaitForEventsAsync(observerPresence, 2);

        observerPresence.Clear();
        await joiner.InvokeAsync("JoinBoard", board.Id);

        var snapshots = await SignalRTestHelper.WaitForEventsAsync(observerPresence, 1);
        var snapshot = snapshots.Should().ContainSingle().Subject;
        snapshot.BoardId.Should().Be(board.Id);
        snapshot.Members.Should().Contain(m => m.UserId == owner.UserId);
        snapshot.Members.Should().Contain(m => m.UserId == member.UserId);
    }

    private static async Task GrantEditorAsync(HttpClient ownerClient, Guid boardId, Guid memberUserId)
    {
        var grantResponse = await ownerClient.PostAsJsonAsync(
            $"/api/boards/{boardId}/access",
            new GrantAccessDto(boardId, memberUserId, UserRole.Editor));
        grantResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
