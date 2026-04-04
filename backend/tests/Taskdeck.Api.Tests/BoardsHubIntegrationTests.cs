using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Taskdeck.Api.Realtime;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Integration tests for the BoardsHub SignalR hub covering presence lifecycle,
/// authentication enforcement, and edge cases.
/// Uses WebApplicationFactory with the real SignalR pipeline (in-memory transport).
/// </summary>
public class BoardsHubIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public BoardsHubIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ────────────────────────────────────────────────────────────────
    // Authentication
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Unauthenticated_Connection_ShouldFailToStart()
    {
        await using var connection = SignalRTestHelper.CreateBoardsHubConnection(_factory, accessToken: null);

        var act = () => connection.StartAsync();
        // SignalR negotiate returns 401; the client wraps this in HttpRequestException
        await act.Should().ThrowAsync<HttpRequestException>()
            .Where(ex => ex.Message.Contains("401") || ex.StatusCode == HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Authenticated_Connection_ShouldSucceed()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "hub-auth");

        await using var connection = SignalRTestHelper.CreateBoardsHubConnection(_factory, user.Token);
        await connection.StartAsync();

        connection.State.Should().Be(HubConnectionState.Connected);
    }

    [Fact]
    public async Task InvalidToken_Connection_ShouldFailToStart()
    {
        await using var connection = SignalRTestHelper.CreateBoardsHubConnection(_factory, "not-a-valid-jwt");

        var act = () => connection.StartAsync();
        // Invalid JWT causes 401 on the negotiate endpoint
        await act.Should().ThrowAsync<HttpRequestException>()
            .Where(ex => ex.Message.Contains("401") || ex.StatusCode == HttpStatusCode.Unauthorized);
    }

    // ────────────────────────────────────────────────────────────────
    // Presence Lifecycle
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task JoinBoard_ShouldBroadcastPresenceSnapshot_WithJoinedUser()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "hub-join");
        var board = await ApiTestHarness.CreateBoardAsync(client, "hub-join-board");

        var events = new EventCollector<BoardPresenceSnapshot>();
        await using var connection = SignalRTestHelper.CreateBoardsHubConnection(_factory, user.Token);
        connection.On<BoardPresenceSnapshot>("boardPresence", snapshot => events.Add(snapshot));
        await connection.StartAsync();

        await connection.InvokeAsync("JoinBoard", board.Id);

        var collected = await SignalRTestHelper.WaitForEventsAsync(events, 1);
        collected.Should().HaveCountGreaterOrEqualTo(1);

        var snapshot = collected.Last();
        snapshot.BoardId.Should().Be(board.Id);
        snapshot.Members.Should().ContainSingle(m => m.UserId == user.UserId);
        snapshot.Members.Single().EditingCardId.Should().BeNull();
    }

    [Fact]
    public async Task SetEditingCard_ShouldBroadcastPresenceWithEditingState()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "hub-edit");
        var board = await ApiTestHarness.CreateBoardAsync(client, "hub-edit-board");

        var colResponse = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Backlog", null, null));
        colResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var column = await colResponse.Content.ReadFromJsonAsync<ColumnDto>();

        var cardResponse = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/cards",
            new CreateCardDto(board.Id, column!.Id, "Test Card", null, null, null));
        cardResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var card = await cardResponse.Content.ReadFromJsonAsync<CardDto>();

        var events = new EventCollector<BoardPresenceSnapshot>();
        await using var connection = SignalRTestHelper.CreateBoardsHubConnection(_factory, user.Token);
        connection.On<BoardPresenceSnapshot>("boardPresence", snapshot => events.Add(snapshot));
        await connection.StartAsync();
        await connection.InvokeAsync("JoinBoard", board.Id);
        await SignalRTestHelper.WaitForEventsAsync(events, 1);
        events.Clear();

        await connection.InvokeAsync("SetEditingCard", board.Id, card!.Id);

        var collected = await SignalRTestHelper.WaitForEventsAsync(events, 1);
        collected.Should().HaveCountGreaterOrEqualTo(1);
        collected.Last().Members.Should().ContainSingle(m =>
            m.UserId == user.UserId && m.EditingCardId == card.Id);
    }

    [Fact]
    public async Task ClearEditingCard_ShouldBroadcastPresenceWithNullEditingState()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "hub-clear");
        var board = await ApiTestHarness.CreateBoardAsync(client, "hub-clear-board");

        var colResponse = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new CreateColumnDto(board.Id, "Backlog", null, null));
        colResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var column = await colResponse.Content.ReadFromJsonAsync<ColumnDto>();
        var cardResponse = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/cards",
            new CreateCardDto(board.Id, column!.Id, "Test Card", null, null, null));
        cardResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var card = await cardResponse.Content.ReadFromJsonAsync<CardDto>();

        var events = new EventCollector<BoardPresenceSnapshot>();
        await using var connection = SignalRTestHelper.CreateBoardsHubConnection(_factory, user.Token);
        connection.On<BoardPresenceSnapshot>("boardPresence", snapshot => events.Add(snapshot));
        await connection.StartAsync();
        await connection.InvokeAsync("JoinBoard", board.Id);
        await SignalRTestHelper.WaitForEventsAsync(events, 1);

        events.Clear();
        await connection.InvokeAsync("SetEditingCard", board.Id, card!.Id);
        await SignalRTestHelper.WaitForEventsAsync(events, 1);

        events.Clear();
        await connection.InvokeAsync("SetEditingCard", board.Id, (Guid?)null);

        var collected = await SignalRTestHelper.WaitForEventsAsync(events, 1);
        collected.Should().HaveCountGreaterOrEqualTo(1);
        collected.Last().Members.Should().ContainSingle(m =>
            m.UserId == user.UserId && m.EditingCardId == null);
    }

    [Fact]
    public async Task LeaveBoard_ShouldRemoveUserFromPresence()
    {
        // The leaving user is removed from the SignalR group before the snapshot is published,
        // so they won't receive the leave snapshot. We use a second observer to verify.
        using var client1 = _factory.CreateClient();
        var user1 = await ApiTestHarness.AuthenticateAsync(client1, "hub-leave1");
        var board = await ApiTestHarness.CreateBoardAsync(client1, "hub-leave-board");

        using var client2 = _factory.CreateClient();
        var user2 = await ApiTestHarness.AuthenticateAsync(client2, "hub-leave2");
        var grantResponse = await client1.PostAsJsonAsync(
            $"/api/boards/{board.Id}/access",
            new GrantAccessDto(board.Id, user2.UserId, UserRole.Editor));
        grantResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "granting board access is a precondition for this test");

        // Observer (user1) joins board
        var observerEvents = new EventCollector<BoardPresenceSnapshot>();
        await using var observer = SignalRTestHelper.CreateBoardsHubConnection(_factory, user1.Token);
        observer.On<BoardPresenceSnapshot>("boardPresence", snapshot => observerEvents.Add(snapshot));
        await observer.StartAsync();
        await observer.InvokeAsync("JoinBoard", board.Id);
        await SignalRTestHelper.WaitForEventsAsync(observerEvents, 1);

        // User2 joins, observer sees 2 members
        await using var conn2 = SignalRTestHelper.CreateBoardsHubConnection(_factory, user2.Token);
        conn2.On<BoardPresenceSnapshot>("boardPresence", _ => { });
        await conn2.StartAsync();
        await conn2.InvokeAsync("JoinBoard", board.Id);

        var afterJoin = await SignalRTestHelper.WaitForEventsAsync(observerEvents, 2);
        afterJoin.Last().Members.Should().HaveCount(2);

        // User2 leaves, observer should see only 1 member
        observerEvents.Clear();
        await conn2.InvokeAsync("LeaveBoard", board.Id);

        var afterLeave = await SignalRTestHelper.WaitForEventsAsync(observerEvents, 1);
        afterLeave.Should().HaveCountGreaterOrEqualTo(1);
        afterLeave.Last().BoardId.Should().Be(board.Id);
        afterLeave.Last().Members.Should().ContainSingle(m => m.UserId == user1.UserId);
    }

    [Fact]
    public async Task AbruptDisconnect_ShouldCleanUpPresence()
    {
        // Two users on the same board, one disconnects abruptly
        using var client1 = _factory.CreateClient();
        var user1 = await ApiTestHarness.AuthenticateAsync(client1, "hub-disc1");
        var board = await ApiTestHarness.CreateBoardAsync(client1, "hub-disc-board");

        using var client2 = _factory.CreateClient();
        var user2 = await ApiTestHarness.AuthenticateAsync(client2, "hub-disc2");
        var grantResponse = await client1.PostAsJsonAsync(
            $"/api/boards/{board.Id}/access",
            new GrantAccessDto(board.Id, user2.UserId, UserRole.Editor));
        grantResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "granting board access is a precondition for this test");

        // User1 connects and joins
        var events1 = new EventCollector<BoardPresenceSnapshot>();
        await using var conn1 = SignalRTestHelper.CreateBoardsHubConnection(_factory, user1.Token);
        conn1.On<BoardPresenceSnapshot>("boardPresence", snapshot => events1.Add(snapshot));
        await conn1.StartAsync();
        await conn1.InvokeAsync("JoinBoard", board.Id);
        await SignalRTestHelper.WaitForEventsAsync(events1, 1);

        // User2 connects and joins
        var conn2 = SignalRTestHelper.CreateBoardsHubConnection(_factory, user2.Token);
        conn2.On<BoardPresenceSnapshot>("boardPresence", _ => { });
        await conn2.StartAsync();
        await conn2.InvokeAsync("JoinBoard", board.Id);

        // Wait for user1 to see user2's join (total 2 events: own join + user2 join)
        var beforeDisconnect = await SignalRTestHelper.WaitForEventsAsync(events1, 2);
        beforeDisconnect.Last().Members.Should().HaveCount(2,
            "both users should be visible in the presence snapshot before disconnect");

        // Abrupt disconnect (dispose without LeaveBoard)
        events1.Clear();
        await conn2.DisposeAsync();

        // User1 should receive a snapshot without user2
        var afterDisconnect = await SignalRTestHelper.WaitForEventsAsync(events1, 1);
        afterDisconnect.Should().HaveCountGreaterOrEqualTo(1);
        afterDisconnect.Last().Members.Should().ContainSingle(m => m.UserId == user1.UserId);
        afterDisconnect.Last().Members.Should().NotContain(m => m.UserId == user2.UserId);
    }

    [Fact]
    public async Task MultipleUsersOnSameBoard_ShouldSeeAllMembersInPresence()
    {
        using var client1 = _factory.CreateClient();
        var user1 = await ApiTestHarness.AuthenticateAsync(client1, "hub-multi1");
        var board = await ApiTestHarness.CreateBoardAsync(client1, "hub-multi-board");

        using var client2 = _factory.CreateClient();
        var user2 = await ApiTestHarness.AuthenticateAsync(client2, "hub-multi2");
        var grantResponse = await client1.PostAsJsonAsync(
            $"/api/boards/{board.Id}/access",
            new GrantAccessDto(board.Id, user2.UserId, UserRole.Editor));
        grantResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "granting board access is a precondition for this test");

        var events1 = new EventCollector<BoardPresenceSnapshot>();
        await using var conn1 = SignalRTestHelper.CreateBoardsHubConnection(_factory, user1.Token);
        conn1.On<BoardPresenceSnapshot>("boardPresence", snapshot => events1.Add(snapshot));
        await conn1.StartAsync();
        await conn1.InvokeAsync("JoinBoard", board.Id);
        await SignalRTestHelper.WaitForEventsAsync(events1, 1);

        // User2 joins
        var events2 = new EventCollector<BoardPresenceSnapshot>();
        await using var conn2 = SignalRTestHelper.CreateBoardsHubConnection(_factory, user2.Token);
        conn2.On<BoardPresenceSnapshot>("boardPresence", snapshot => events2.Add(snapshot));
        await conn2.StartAsync();
        await conn2.InvokeAsync("JoinBoard", board.Id);

        // User1 sees both
        var collected1 = await SignalRTestHelper.WaitForEventsAsync(events1, 2);
        collected1.Last().Members.Should().HaveCount(2);
        collected1.Last().Members.Should().Contain(m => m.UserId == user1.UserId);
        collected1.Last().Members.Should().Contain(m => m.UserId == user2.UserId);

        // User2 also sees both
        var collected2 = await SignalRTestHelper.WaitForEventsAsync(events2, 1);
        collected2.Last().Members.Should().HaveCount(2);
    }

    // ────────────────────────────────────────────────────────────────
    // Authorization enforcement
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task JoinBoard_WithoutAccess_ShouldThrowHubException()
    {
        using var client1 = _factory.CreateClient();
        var user1 = await ApiTestHarness.AuthenticateAsync(client1, "hub-noauthz1");
        var board = await ApiTestHarness.CreateBoardAsync(client1, "hub-noauthz-board");

        using var client2 = _factory.CreateClient();
        var user2 = await ApiTestHarness.AuthenticateAsync(client2, "hub-noauthz2");

        await using var connection = SignalRTestHelper.CreateBoardsHubConnection(_factory, user2.Token);
        await connection.StartAsync();

        var act = () => connection.InvokeAsync("JoinBoard", board.Id);
        await act.Should().ThrowAsync<HubException>()
            .WithMessage("*Forbidden*");
    }

    [Fact]
    public async Task LeaveBoard_WithoutAccess_ShouldThrowHubException()
    {
        using var client1 = _factory.CreateClient();
        var user1 = await ApiTestHarness.AuthenticateAsync(client1, "hub-lvnoauthz1");
        var board = await ApiTestHarness.CreateBoardAsync(client1, "hub-lvnoauthz-board");

        using var client2 = _factory.CreateClient();
        var user2 = await ApiTestHarness.AuthenticateAsync(client2, "hub-lvnoauthz2");

        await using var connection = SignalRTestHelper.CreateBoardsHubConnection(_factory, user2.Token);
        await connection.StartAsync();

        var act = () => connection.InvokeAsync("LeaveBoard", board.Id);
        await act.Should().ThrowAsync<HubException>()
            .WithMessage("*Forbidden*");
    }

    [Fact]
    public async Task SetEditingCard_WithoutJoiningBoard_ShouldThrowHubException()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "hub-nojoin");
        var board = await ApiTestHarness.CreateBoardAsync(client, "hub-nojoin-board");

        await using var connection = SignalRTestHelper.CreateBoardsHubConnection(_factory, user.Token);
        await connection.StartAsync();

        var act = () => connection.InvokeAsync("SetEditingCard", board.Id, Guid.NewGuid());
        await act.Should().ThrowAsync<HubException>()
            .WithMessage("*Join the board before sharing editing status*");
    }

    // ────────────────────────────────────────────────────────────────
    // Edge cases
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task JoinBoard_SwitchesBoardPresence_WhenSameConnectionJoinsNewBoard()
    {
        // InMemoryBoardPresenceTracker auto-removes from previous board
        // when a single connection joins a different board.
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "hub-switch");
        var boardA = await ApiTestHarness.CreateBoardAsync(client, "hub-board-a");
        var boardB = await ApiTestHarness.CreateBoardAsync(client, "hub-board-b");

        var events = new EventCollector<BoardPresenceSnapshot>();
        await using var connection = SignalRTestHelper.CreateBoardsHubConnection(_factory, user.Token);
        connection.On<BoardPresenceSnapshot>("boardPresence", snapshot => events.Add(snapshot));
        await connection.StartAsync();

        await connection.InvokeAsync("JoinBoard", boardA.Id);
        await SignalRTestHelper.WaitForEventsAsync(events, 1);

        events.Clear();
        await connection.InvokeAsync("JoinBoard", boardB.Id);
        var collected = await SignalRTestHelper.WaitForEventsAsync(events, 1);

        collected.Last().BoardId.Should().Be(boardB.Id);
        collected.Last().Members.Should().ContainSingle(m => m.UserId == user.UserId);
    }

    [Fact]
    public async Task SameUser_TwoConnections_SameBoardPresenceAggregated()
    {
        // Same user opens two connections — member list shows user once (aggregated by userId)
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "hub-twotabs");
        var board = await ApiTestHarness.CreateBoardAsync(client, "hub-twotabs-board");

        var events1 = new EventCollector<BoardPresenceSnapshot>();
        await using var conn1 = SignalRTestHelper.CreateBoardsHubConnection(_factory, user.Token);
        conn1.On<BoardPresenceSnapshot>("boardPresence", snapshot => events1.Add(snapshot));
        await conn1.StartAsync();
        await conn1.InvokeAsync("JoinBoard", board.Id);
        await SignalRTestHelper.WaitForEventsAsync(events1, 1);

        await using var conn2 = SignalRTestHelper.CreateBoardsHubConnection(_factory, user.Token);
        conn2.On<BoardPresenceSnapshot>("boardPresence", _ => { });
        await conn2.StartAsync();

        events1.Clear();
        await conn2.InvokeAsync("JoinBoard", board.Id);

        var collected = await SignalRTestHelper.WaitForEventsAsync(events1, 1);
        collected.Last().Members.Should().ContainSingle(m => m.UserId == user.UserId);
    }

    [Fact]
    public async Task SameUser_TwoConnections_DisconnectOne_ShouldNotRemovePresence()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "hub-twotab-disc");
        var board = await ApiTestHarness.CreateBoardAsync(client, "hub-twotab-disc-board");

        var events1 = new EventCollector<BoardPresenceSnapshot>();
        await using var conn1 = SignalRTestHelper.CreateBoardsHubConnection(_factory, user.Token);
        conn1.On<BoardPresenceSnapshot>("boardPresence", snapshot => events1.Add(snapshot));
        await conn1.StartAsync();
        await conn1.InvokeAsync("JoinBoard", board.Id);
        await SignalRTestHelper.WaitForEventsAsync(events1, 1);

        await using var conn2 = SignalRTestHelper.CreateBoardsHubConnection(_factory, user.Token);
        conn2.On<BoardPresenceSnapshot>("boardPresence", _ => { });
        await conn2.StartAsync();
        await conn2.InvokeAsync("JoinBoard", board.Id);
        await SignalRTestHelper.WaitForEventsAsync(events1, 2);

        // Disconnect conn2 (DisposeAsync is idempotent; await using provides safety net)
        events1.Clear();
        await conn2.DisposeAsync();

        // User should still be present via conn1
        var collected = await SignalRTestHelper.WaitForEventsAsync(events1, 1);
        collected.Last().Members.Should().ContainSingle(m => m.UserId == user.UserId);
    }

    [Fact]
    public async Task JoinBoard_NonExistentBoardId_ShouldThrowHubException()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "hub-nonexist");

        await using var connection = SignalRTestHelper.CreateBoardsHubConnection(_factory, user.Token);
        await connection.StartAsync();

        var act = () => connection.InvokeAsync("JoinBoard", Guid.NewGuid());
        await act.Should().ThrowAsync<HubException>();
    }

    [Fact]
    public async Task SetEditingCard_EmptyGuid_ShouldClearEditingState()
    {
        // The hub sanitizes Guid.Empty to null
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "hub-emptyguid");
        var board = await ApiTestHarness.CreateBoardAsync(client, "hub-emptyguid-board");

        var events = new EventCollector<BoardPresenceSnapshot>();
        await using var connection = SignalRTestHelper.CreateBoardsHubConnection(_factory, user.Token);
        connection.On<BoardPresenceSnapshot>("boardPresence", snapshot => events.Add(snapshot));
        await connection.StartAsync();
        await connection.InvokeAsync("JoinBoard", board.Id);
        await SignalRTestHelper.WaitForEventsAsync(events, 1);

        events.Clear();
        await connection.InvokeAsync("SetEditingCard", board.Id, Guid.Empty);

        var collected = await SignalRTestHelper.WaitForEventsAsync(events, 1);
        collected.Last().Members.Should().ContainSingle(m =>
            m.UserId == user.UserId && m.EditingCardId == null);
    }

    [Fact]
    public async Task PresenceSnapshot_ShouldContainCorrectTimestamp()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "hub-timestamp");
        var board = await ApiTestHarness.CreateBoardAsync(client, "hub-timestamp-board");

        var events = new EventCollector<BoardPresenceSnapshot>();
        await using var connection = SignalRTestHelper.CreateBoardsHubConnection(_factory, user.Token);
        connection.On<BoardPresenceSnapshot>("boardPresence", snapshot => events.Add(snapshot));

        var beforeJoin = DateTimeOffset.UtcNow.AddSeconds(-1);
        await connection.StartAsync();
        await connection.InvokeAsync("JoinBoard", board.Id);

        var collected = await SignalRTestHelper.WaitForEventsAsync(events, 1);
        collected.Last().OccurredAt.Should().BeAfter(beforeJoin);
        collected.Last().OccurredAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Connection_ShouldNotReceiveEventsFromUnsubscribedBoard()
    {
        // Create two boards: user subscribes to board A only, events on board B should not arrive
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "hub-unsub");
        var boardA = await ApiTestHarness.CreateBoardAsync(client, "hub-unsub-a");
        var boardB = await ApiTestHarness.CreateBoardAsync(client, "hub-unsub-b");

        var events = new EventCollector<BoardPresenceSnapshot>();
        await using var connection = SignalRTestHelper.CreateBoardsHubConnection(_factory, user.Token);
        connection.On<BoardPresenceSnapshot>("boardPresence", snapshot => events.Add(snapshot));
        await connection.StartAsync();

        // Only join board A
        await connection.InvokeAsync("JoinBoard", boardA.Id);
        await SignalRTestHelper.WaitForEventsAsync(events, 1);

        // Another user joins board B (we use the same user with a second connection for simplicity)
        await using var conn2 = SignalRTestHelper.CreateBoardsHubConnection(_factory, user.Token);
        conn2.On<BoardPresenceSnapshot>("boardPresence", _ => { });
        await conn2.StartAsync();
        await conn2.InvokeAsync("JoinBoard", boardB.Id);

        // Give events time to arrive (they shouldn't)
        await Task.Delay(500);

        // Only the initial join event from board A should be present
        var collected = events.ToList();
        collected.Should().HaveCount(1);
        collected.Single().BoardId.Should().Be(boardA.Id);
    }
}
