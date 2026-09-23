using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Moq;
using Taskdeck.Api.Hubs;
using Taskdeck.Api.Realtime;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Xunit;

namespace Taskdeck.Api.Tests;

/// <summary>
/// Hub-level cover for the JoinBoard switch safety net: a JoinBoard that arrives
/// without a matching LeaveBoard must not strand the connection in the old
/// SignalR group with ghost presence.
/// </summary>
public class BoardsHubSwitchTests
{
    private const string ConnectionId = "conn-1";

    [Fact]
    public async Task JoinBoard_ShouldLeavePreviousGroupAndRepublishOldSnapshot_WhenSwitchingWithoutLeave()
    {
        // Arrange
        var boardA = Guid.NewGuid();
        var boardB = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var (hub, groups, groupProxies) = CreateHub(userId);

        await hub.JoinBoard(boardA);

        // Act: switch boards without calling LeaveBoard first.
        await hub.JoinBoard(boardB);

        // Assert: the connection left the old group...
        groups.Verify(
            g => g.RemoveFromGroupAsync(ConnectionId, BoardHubGroups.ForBoard(boardA), default),
            Times.Once);

        // ...the old board's observers saw the departure...
        groupProxies.Should().ContainKey(BoardHubGroups.ForBoard(boardA));
        var oldSnapshots = groupProxies[BoardHubGroups.ForBoard(boardA)].Snapshots
            .Where(s => s.BoardId == boardA)
            .ToList();
        oldSnapshots.Should().NotBeEmpty();
        oldSnapshots.Last().Members.Should().NotContain(m => m.UserId == userId);

        // ...and the new board shows the join.
        var newSnapshots = groupProxies[BoardHubGroups.ForBoard(boardB)].Snapshots
            .Where(s => s.BoardId == boardB)
            .ToList();
        newSnapshots.Should().ContainSingle()
            .Which.Members.Should().ContainSingle(m => m.UserId == userId);
    }

    [Fact]
    public async Task JoinBoard_ShouldNotLeave_WhenRejoiningTheSameBoard()
    {
        // Arrange
        var board = Guid.NewGuid();
        var (hub, groups, _) = CreateHub(Guid.NewGuid());

        await hub.JoinBoard(board);

        // Act
        await hub.JoinBoard(board);

        // Assert
        groups.Verify(
            g => g.RemoveFromGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        groups.Verify(
            g => g.AddToGroupAsync(ConnectionId, BoardHubGroups.ForBoard(board), default),
            Times.Exactly(2));
    }

    [Fact]
    public async Task JoinBoard_ShouldNotLeave_WhenJoiningTheFirstBoard()
    {
        // Arrange
        var (hub, groups, _) = CreateHub(Guid.NewGuid());

        // Act
        await hub.JoinBoard(Guid.NewGuid());

        // Assert
        groups.Verify(
            g => g.RemoveFromGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static (BoardsHub Hub, Mock<IGroupManager> Groups, Dictionary<string, RecordingClientProxy> Proxies)
        CreateHub(Guid userId)
    {
        var authorizationMock = new Mock<IAuthorizationService>();
        authorizationMock
            .Setup(a => a.CanReadBoardAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
            .ReturnsAsync(Result.Success(true));

        var tracker = new InMemoryBoardPresenceTracker();
        var hub = new BoardsHub(authorizationMock.Object, tracker);

        var claims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("name", "Switch Tester"),
        }));
        var contextMock = new Mock<HubCallerContext>();
        contextMock.SetupGet(c => c.ConnectionId).Returns(ConnectionId);
        contextMock.SetupGet(c => c.User).Returns(claims);

        var groupsMock = new Mock<IGroupManager>();
        var proxies = new Dictionary<string, RecordingClientProxy>(StringComparer.Ordinal);
        var clientsMock = new Mock<IHubCallerClients>();
        clientsMock
            .Setup(c => c.Group(It.IsAny<string>()))
            .Returns((string groupName) =>
            {
                if (!proxies.TryGetValue(groupName, out var proxy))
                {
                    proxy = new RecordingClientProxy();
                    proxies[groupName] = proxy;
                }

                return proxy;
            });

        hub.Context = contextMock.Object;
        hub.Groups = groupsMock.Object;
        hub.Clients = clientsMock.Object;

        return (hub, groupsMock, proxies);
    }

    private sealed class RecordingClientProxy : ISingleClientProxy
    {
        public List<BoardPresenceSnapshot> Snapshots { get; } = new();

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            if (string.Equals(method, "boardPresence", StringComparison.Ordinal)
                && args is [BoardPresenceSnapshot snapshot, ..])
            {
                Snapshots.Add(snapshot);
            }

            return Task.CompletedTask;
        }

        public Task<T> InvokeCoreAsync<T>(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<T>(default!);
        }
    }
}
