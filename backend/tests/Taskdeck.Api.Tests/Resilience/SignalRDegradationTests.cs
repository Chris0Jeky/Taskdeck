using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Taskdeck.Api.Realtime;
using Taskdeck.Api.Tests.Support;
using Taskdeck.Application.DTOs;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Api.Tests.Resilience;

/// <summary>
/// Tests that SignalR hub failures on one connection do not cascade to other
/// connected clients, and that invalid operations produce HubException rather
/// than killing the connection.
/// </summary>
public class SignalRDegradationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public SignalRDegradationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── Hub Exception Isolation ────────────────────────────────────────

    [Fact]
    public async Task JoinBoard_WithInvalidBoardId_ThrowsHubExceptionButConnectionSurvives()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "hub-resilience-bad-board");

        await using var connection = SignalRTestHelper.CreateBoardsHubConnection(_factory, user.Token);
        await connection.StartAsync();
        connection.State.Should().Be(HubConnectionState.Connected);

        // Try to join a non-existent board — should throw HubException.
        var act = () => connection.InvokeAsync("JoinBoard", Guid.NewGuid());
        await act.Should().ThrowAsync<HubException>(
            "joining a non-existent board should throw a HubException");

        // Connection should still be alive after the error.
        connection.State.Should().Be(HubConnectionState.Connected,
            "one failed hub invocation should not kill the connection");
    }

    [Fact]
    public async Task SetEditingCard_WithoutJoining_ThrowsHubExceptionButConnectionSurvives()
    {
        using var client = _factory.CreateClient();
        var user = await ApiTestHarness.AuthenticateAsync(client, "hub-resilience-no-join");
        var board = await ApiTestHarness.CreateBoardAsync(client, "hub-resilience-board");

        await using var connection = SignalRTestHelper.CreateBoardsHubConnection(_factory, user.Token);
        await connection.StartAsync();

        // Try editing a card without joining the board first.
        var act = () => connection.InvokeAsync("SetEditingCard", board.Id, Guid.NewGuid());
        await act.Should().ThrowAsync<HubException>(
            "setting editing card without joining should throw a HubException");

        // Connection should still be connected.
        connection.State.Should().Be(HubConnectionState.Connected,
            "hub error should not disconnect the client");
    }

    // ── One Client's Error Doesn't Affect Others ──────────────────────

    [Fact]
    public async Task ErrorOnOneClient_DoesNotDisconnectOtherClients()
    {
        using var client1 = _factory.CreateClient();
        using var client2 = _factory.CreateClient();

        var user1 = await ApiTestHarness.AuthenticateAsync(client1, "hub-resilience-user1");
        var user2 = await ApiTestHarness.AuthenticateAsync(client2, "hub-resilience-user2");

        var board = await ApiTestHarness.CreateBoardAsync(client1, "hub-resilience-multi");

        // Share the board with user2.
        await client1.PostAsJsonAsync(
            $"/api/boards/{board.Id}/access",
            new GrantAccessDto(board.Id, user2.UserId, UserRole.Editor));

        var presenceCollector = new EventCollector<BoardPresenceSnapshot>();

        await using var connection1 = SignalRTestHelper.CreateBoardsHubConnection(_factory, user1.Token);
        await using var connection2 = SignalRTestHelper.CreateBoardsHubConnection(_factory, user2.Token);

        connection1.On<BoardPresenceSnapshot>("boardPresence", snapshot => presenceCollector.Add(snapshot));

        await connection1.StartAsync();
        await connection2.StartAsync();

        // Both users join the board.
        await connection1.InvokeAsync("JoinBoard", board.Id);
        await connection2.InvokeAsync("JoinBoard", board.Id);

        // Wait for presence events to confirm both clients joined (event-based, not timing-based).
        await SignalRTestHelper.WaitForEventsAsync(presenceCollector, 2, TimeSpan.FromSeconds(3));

        // Client 1 causes an error by trying to join a non-existent board.
        var act = () => connection1.InvokeAsync("JoinBoard", Guid.NewGuid());
        try { await act(); } catch (HubException) { /* expected */ }

        // Client 2 should still be connected and functional.
        connection2.State.Should().Be(HubConnectionState.Connected,
            "client 2 should be unaffected by client 1's error");

        // Client 1 should also still be connected (HubException doesn't kill connection).
        connection1.State.Should().Be(HubConnectionState.Connected,
            "client 1's connection should survive its own hub exception");

        // Verify client 2 can still perform operations on the hub.
        var postErrorAct = () => connection2.InvokeAsync("SetEditingCard", board.Id, (Guid?)null);
        await postErrorAct.Should().NotThrowAsync(
            "client 2 should be fully functional after client 1's error");
    }

    // ── Disconnection Handling ────────────────────────────────────────

    [Fact]
    public async Task DisconnectedClient_RemovedFromPresence_OtherClientsNotified()
    {
        using var client1 = _factory.CreateClient();
        using var client2 = _factory.CreateClient();

        var user1 = await ApiTestHarness.AuthenticateAsync(client1, "hub-disconnect-user1");
        var user2 = await ApiTestHarness.AuthenticateAsync(client2, "hub-disconnect-user2");

        var board = await ApiTestHarness.CreateBoardAsync(client1, "hub-disconnect-board");

        await client1.PostAsJsonAsync(
            $"/api/boards/{board.Id}/access",
            new GrantAccessDto(board.Id, user2.UserId, UserRole.Editor));

        var presenceCollector = new EventCollector<BoardPresenceSnapshot>();

        await using var connection1 = SignalRTestHelper.CreateBoardsHubConnection(_factory, user1.Token);
        var connection2 = SignalRTestHelper.CreateBoardsHubConnection(_factory, user2.Token);

        connection1.On<BoardPresenceSnapshot>("boardPresence", snapshot => presenceCollector.Add(snapshot));

        await connection1.StartAsync();
        await connection2.StartAsync();

        await connection1.InvokeAsync("JoinBoard", board.Id);
        await connection2.InvokeAsync("JoinBoard", board.Id);

        // Wait for join events.
        await SignalRTestHelper.WaitForEventsAsync(presenceCollector, 2, TimeSpan.FromSeconds(3));
        presenceCollector.Clear();

        // Disconnect client 2 explicitly.
        await connection2.DisposeAsync();

        // Client 1 should receive a presence update showing client 2 left.
        var disconnectEvents = await SignalRTestHelper.WaitForEventsAsync(
            presenceCollector, 1, TimeSpan.FromSeconds(5));

        disconnectEvents.Should().HaveCountGreaterThanOrEqualTo(1,
            "client 1 should be notified when client 2 disconnects");

        // The latest presence snapshot should no longer include client 2.
        var latestPresence = disconnectEvents.Last();
        latestPresence.Members.Should().NotContain(u => u.UserId == user2.UserId,
            "disconnected user should be removed from presence");
    }
}
