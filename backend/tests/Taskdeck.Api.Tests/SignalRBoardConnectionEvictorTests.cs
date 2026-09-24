using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Taskdeck.Api.Hubs;
using Taskdeck.Api.Realtime;
using Xunit;

namespace Taskdeck.Api.Tests;

public class SignalRBoardConnectionEvictorTests
{
    [Fact]
    public async Task EvictUserFromBoardAsync_ShouldRemoveConnectionsPublishSnapshotAndNotifyEvicted()
    {
        var boardId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var tracker = new InMemoryBoardPresenceTracker();
        tracker.Join(boardId, "conn-1", userId, "evicted");
        tracker.Join(boardId, "conn-2", Guid.NewGuid(), "remaining");
        var groups = new RecordingGroupManager();
        var clients = new RecordingHubClients();
        var evictor = new SignalRBoardConnectionEvictor(new FakeEvictHubContext(groups, clients), tracker);

        await evictor.EvictUserFromBoardAsync(boardId, userId);

        var group = BoardHubGroups.ForBoard(boardId);
        groups.Removed.Should().ContainSingle().Which.Should().Be(("conn-1", group));
        var groupCall = clients.GroupCalls.Should().ContainSingle().Subject;
        groupCall.GroupName.Should().Be(group);
        groupCall.Proxy.MethodName.Should().Be("boardPresence");
        var directCall = clients.DirectCalls.Should().ContainSingle().Subject;
        directCall.ConnectionIds.Should().BeEquivalentTo("conn-1");
        directCall.Proxy.MethodName.Should().Be("accessRevoked");
    }

    [Fact]
    public async Task EvictUserFromBoardAsync_ShouldDoNothing_WhenUserHasNoConnections()
    {
        var tracker = new InMemoryBoardPresenceTracker();
        var groups = new RecordingGroupManager();
        var clients = new RecordingHubClients();
        var evictor = new SignalRBoardConnectionEvictor(new FakeEvictHubContext(groups, clients), tracker);

        await evictor.EvictUserFromBoardAsync(Guid.NewGuid(), Guid.NewGuid());

        groups.Removed.Should().BeEmpty();
        clients.GroupCalls.Should().BeEmpty();
        clients.DirectCalls.Should().BeEmpty();
    }

    private sealed class FakeEvictHubContext : IHubContext<BoardsHub>
    {
        public FakeEvictHubContext(IGroupManager groups, IHubClients clients)
        {
            Groups = groups;
            Clients = clients;
        }

        public IHubClients Clients { get; }

        public IGroupManager Groups { get; }
    }

    private sealed class RecordingGroupManager : IGroupManager
    {
        public List<(string ConnectionId, string GroupName)> Removed { get; } = new();

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Removed.Add((connectionId, groupName));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingHubClients : IHubClients
    {
        public List<(string GroupName, RecordingProxy Proxy)> GroupCalls { get; } = new();

        public List<(IReadOnlyList<string> ConnectionIds, RecordingProxy Proxy)> DirectCalls { get; } = new();

        public IClientProxy All => throw new NotSupportedException();

        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();

        public IClientProxy Client(string connectionId) => throw new NotSupportedException();

        public IClientProxy Clients(IReadOnlyList<string> connectionIds)
        {
            var proxy = new RecordingProxy();
            DirectCalls.Add((connectionIds, proxy));
            return proxy;
        }

        public IClientProxy Group(string groupName)
        {
            var proxy = new RecordingProxy();
            GroupCalls.Add((groupName, proxy));
            return proxy;
        }

        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();

        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();

        public IClientProxy User(string userId) => throw new NotSupportedException();

        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }

    private sealed class RecordingProxy : IClientProxy
    {
        public string? MethodName { get; private set; }

        public IReadOnlyList<object?> Arguments { get; private set; } = [];

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            MethodName = method;
            Arguments = args;
            return Task.CompletedTask;
        }
    }
}
