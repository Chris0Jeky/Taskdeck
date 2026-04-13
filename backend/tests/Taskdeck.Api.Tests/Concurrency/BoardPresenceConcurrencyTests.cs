using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Taskdeck.Api.Realtime;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Api.Tests.Concurrency;

/// <summary>
/// Board presence (SignalR) concurrency tests exercising:
/// - Rapid join/leave stress (multiple connections join and leave rapidly)
/// - Disconnect during edit (editing state cleared on abrupt disconnect)
///
/// These tests validate that presence tracking is eventually consistent
/// under concurrent SignalR operations.
///
/// See GitHub issue #705 (TST-55).
/// </summary>
public class BoardPresenceConcurrencyTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public BoardPresenceConcurrencyTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Polls the observer's event collector until a snapshot with the expected
    /// member count appears, or the timeout elapses.
    /// </summary>
    private static async Task<BoardPresenceSnapshot> WaitForPresenceCountAsync(
        EventCollector<BoardPresenceSnapshot> events,
        int expectedMemberCount,
        TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(10);
        var deadline = DateTimeOffset.UtcNow + effectiveTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var snapshot = events.ToList().LastOrDefault();
            if (snapshot is not null && snapshot.Members.Count == expectedMemberCount)
                return snapshot;
            await Task.Delay(50);
        }

        var last = events.ToList().LastOrDefault();
        var actualCount = last?.Members.Count ?? 0;
        throw new TimeoutException(
            $"Expected presence snapshot with {expectedMemberCount} members " +
            $"but last snapshot had {actualCount} within {effectiveTimeout.TotalSeconds}s.");
    }

    /// <summary>
    /// Scenario: Rapid join/leave stress.
    /// Multiple connections rapidly join and leave a board.
    /// After all connections settle, the final presence snapshot should be
    /// eventually consistent (only connections that remain joined are present).
    /// </summary>
    [Fact]
    public async Task RapidJoinLeave_EventuallyConsistent()
    {
        const int connectionCount = 5;

        using var ownerClient = _factory.CreateClient();
        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "presence-rapid");
        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "presence-rapid-board");

        // Create users and grant access
        using var setupClient = _factory.CreateClient();
        var users = new List<TestUserContext>();
        for (var i = 0; i < connectionCount; i++)
        {
            var u = await ApiTestHarness.AuthenticateAsync(setupClient, $"presence-rapid-{i}");
            var grant = await ownerClient.PostAsJsonAsync(
                $"/api/boards/{board.Id}/access",
                new GrantAccessDto(board.Id, u.UserId, UserRole.Editor));
            grant.StatusCode.Should().Be(HttpStatusCode.OK);
            users.Add(u);
        }

        // Owner observes presence events
        var observerEvents = new EventCollector<BoardPresenceSnapshot>();
        await using var observer = SignalRTestHelper.CreateBoardsHubConnection(
            _factory, owner.Token);
        observer.On<BoardPresenceSnapshot>("boardPresence",
            snapshot => observerEvents.Add(snapshot));
        await observer.StartAsync();
        await observer.InvokeAsync("JoinBoard", board.Id);
        await SignalRTestHelper.WaitForEventsAsync(observerEvents, 1);
        observerEvents.Clear();

        // All users join simultaneously via Barrier
        var connections = new List<HubConnection>();
        try
        {
            using var joinBarrier = new Barrier(connectionCount + 1);
            var joinTasks = users.Select(async user =>
            {
                var conn = SignalRTestHelper.CreateBoardsHubConnection(_factory, user.Token);
                conn.On<BoardPresenceSnapshot>("boardPresence", _ => { });
                await conn.StartAsync();
                lock (connections) { connections.Add(conn); }
                joinBarrier.SignalAndWait(TimeSpan.FromSeconds(10));
                await conn.InvokeAsync("JoinBoard", board.Id);
            }).ToArray();

            joinBarrier.SignalAndWait(TimeSpan.FromSeconds(10));
            await Task.WhenAll(joinTasks);

            // Wait for all joins to settle
            var afterJoin = await WaitForPresenceCountAsync(
                observerEvents, connectionCount + 1, TimeSpan.FromSeconds(10));
            afterJoin.Members.Should().HaveCount(connectionCount + 1,
                "all joined users plus the observer owner should be present");

            // First half leave rapidly
            observerEvents.Clear();
            var leavingCount = connectionCount / 2;
            using var leaveBarrier = new Barrier(leavingCount + 1);
            var leaveTasks = connections.Take(leavingCount).Select(async conn =>
            {
                leaveBarrier.SignalAndWait(TimeSpan.FromSeconds(10));
                await conn.InvokeAsync("LeaveBoard", board.Id);
            }).ToArray();

            leaveBarrier.SignalAndWait(TimeSpan.FromSeconds(10));
            await Task.WhenAll(leaveTasks);

            // Wait for leaves to settle
            var remaining = connectionCount - leavingCount;
            var afterLeave = await WaitForPresenceCountAsync(
                observerEvents, remaining + 1, TimeSpan.FromSeconds(10));
            afterLeave.Members.Should().HaveCount(remaining + 1,
                $"after {leavingCount} leaves, {remaining} users + owner should remain");
        }
        finally
        {
            foreach (var conn in connections)
                await conn.DisposeAsync();
        }
    }

    /// <summary>
    /// Scenario: Disconnect during edit clears editing state.
    /// A user sets an editing card, then their connection drops abruptly.
    /// The presence snapshot should no longer include the editing state.
    /// </summary>
    [Fact]
    public async Task DisconnectDuringEdit_ClearsEditingState()
    {
        using var ownerClient = _factory.CreateClient();
        var owner = await ApiTestHarness.AuthenticateAsync(ownerClient, "presence-disc-edit");
        var board = await ApiTestHarness.CreateBoardAsync(ownerClient, "presence-disc-board");

        // Create a column and card
        var colResp = await ownerClient.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Backlog", null, null));
        colResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var col = await colResp.Content.ReadFromJsonAsync<ColumnDto>();

        var cardResp = await ownerClient.PostAsJsonAsync(
            $"/api/boards/{board.Id}/cards",
            new CreateCardDto(board.Id, col!.Id, "Disconnect card", null, null, null));
        cardResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var card = await cardResp.Content.ReadFromJsonAsync<CardDto>();

        // Second user who will disconnect
        using var editorClient = _factory.CreateClient();
        var editor = await ApiTestHarness.AuthenticateAsync(editorClient, "presence-disc-editor");
        var grant = await ownerClient.PostAsJsonAsync(
            $"/api/boards/{board.Id}/access",
            new GrantAccessDto(board.Id, editor.UserId, UserRole.Editor));
        grant.StatusCode.Should().Be(HttpStatusCode.OK);

        // Owner joins and observes
        var observerEvents = new EventCollector<BoardPresenceSnapshot>();
        await using var observer = SignalRTestHelper.CreateBoardsHubConnection(
            _factory, owner.Token);
        observer.On<BoardPresenceSnapshot>("boardPresence",
            snapshot => observerEvents.Add(snapshot));
        await observer.StartAsync();
        await observer.InvokeAsync("JoinBoard", board.Id);
        await SignalRTestHelper.WaitForEventsAsync(observerEvents, 1);

        // Editor joins and starts editing
        await using var editorConn = SignalRTestHelper.CreateBoardsHubConnection(
            _factory, editor.Token);
        editorConn.On<BoardPresenceSnapshot>("boardPresence", _ => { });
        await editorConn.StartAsync();
        await editorConn.InvokeAsync("JoinBoard", board.Id);
        await SignalRTestHelper.WaitForEventsAsync(observerEvents, 2);

        observerEvents.Clear();
        await editorConn.InvokeAsync("SetEditingCard", board.Id, card!.Id);

        // Wait for editing presence update
        var editingEvents = await SignalRTestHelper.WaitForEventsAsync(observerEvents, 1);
        var editorMember = editingEvents.Last().Members
            .FirstOrDefault(m => m.UserId == editor.UserId);
        editorMember.Should().NotBeNull("editor should be visible in presence");
        editorMember!.EditingCardId.Should().Be(card.Id,
            "editor should show as editing the card");

        // Abrupt disconnect (no LeaveBoard, no SetEditingCard(null))
        observerEvents.Clear();
        await editorConn.DisposeAsync();

        // Owner should receive a snapshot without the editor
        var afterDisconnect = await SignalRTestHelper.WaitForEventsAsync(
            observerEvents, 1, TimeSpan.FromSeconds(5));
        afterDisconnect.Last().Members.Should().NotContain(
            m => m.UserId == editor.UserId,
            "disconnected editor should be removed from presence");
        afterDisconnect.Last().Members.Should().ContainSingle(
            m => m.UserId == owner.UserId,
            "only the owner should remain after editor disconnects");
    }
}
