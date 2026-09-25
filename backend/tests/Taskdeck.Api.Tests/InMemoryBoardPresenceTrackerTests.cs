using FluentAssertions;
using Taskdeck.Api.Realtime;
using Xunit;

namespace Taskdeck.Api.Tests;

public class InMemoryBoardPresenceTrackerTests
{
    [Fact]
    public void Join_ShouldRemoveConnectionFromPreviousBoard_WhenRejoiningDifferentBoard()
    {
        var tracker = new InMemoryBoardPresenceTracker();
        var boardA = Guid.NewGuid();
        var boardB = Guid.NewGuid();
        const string connectionId = "conn-1";
        var userId = Guid.NewGuid();

        tracker.Join(boardA, connectionId, userId, "user");
        tracker.Join(boardB, connectionId, userId, "user");

        var boardASnapshot = tracker.Leave(boardA, "non-member");

        boardASnapshot.Members.Should().BeEmpty();
        tracker.IsConnectionJoinedBoard(connectionId, boardB).Should().BeTrue();
    }

    [Fact]
    public void Leave_ShouldNotDropReverseMap_WhenConnectionNotInRequestedBoard()
    {
        var tracker = new InMemoryBoardPresenceTracker();
        var boardA = Guid.NewGuid();
        var boardB = Guid.NewGuid();
        const string connectionA = "conn-a";
        const string connectionB = "conn-b";
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        tracker.Join(boardA, connectionA, userA, "user-a");
        tracker.Join(boardB, connectionB, userB, "user-b");

        _ = tracker.Leave(boardB, connectionA);

        tracker.IsConnectionJoinedBoard(connectionA, boardA).Should().BeTrue();
        var leaveConnectionSnapshot = tracker.LeaveConnection(connectionA);
        leaveConnectionSnapshot.Should().NotBeNull();
        leaveConnectionSnapshot!.BoardId.Should().Be(boardA);
    }

    [Fact]
    public void EvictUser_ShouldRemoveOnlyThatUsersConnections_AndReturnSnapshot()
    {
        var tracker = new InMemoryBoardPresenceTracker();
        var boardId = Guid.NewGuid();
        var evictedUser = Guid.NewGuid();
        var remainingUser = Guid.NewGuid();

        tracker.Join(boardId, "conn-evict-1", evictedUser, "evicted");
        tracker.Join(boardId, "conn-evict-2", evictedUser, "evicted");
        tracker.Join(boardId, "conn-keep", remainingUser, "remaining");

        var eviction = tracker.EvictUser(boardId, evictedUser);

        eviction.EvictedConnectionIds.Should().BeEquivalentTo("conn-evict-1", "conn-evict-2");
        eviction.Snapshot.BoardId.Should().Be(boardId);
        eviction.Snapshot.Members.Should().ContainSingle(m => m.UserId == remainingUser);
        tracker.IsConnectionJoinedBoard("conn-evict-1", boardId).Should().BeFalse();
        tracker.IsConnectionJoinedBoard("conn-evict-2", boardId).Should().BeFalse();
        tracker.IsConnectionJoinedBoard("conn-keep", boardId).Should().BeTrue();
    }

    [Fact]
    public void EvictUser_ShouldReturnEmptySnapshot_WhenUserHasNoConnections()
    {
        var tracker = new InMemoryBoardPresenceTracker();
        var boardId = Guid.NewGuid();

        var eviction = tracker.EvictUser(boardId, Guid.NewGuid());

        eviction.EvictedConnectionIds.Should().BeEmpty();
        eviction.Snapshot.BoardId.Should().Be(boardId);
        eviction.Snapshot.Members.Should().BeEmpty();
    }

    [Fact]
    public void EvictUser_ShouldNotTouchOtherBoards()
    {
        var tracker = new InMemoryBoardPresenceTracker();
        var boardA = Guid.NewGuid();
        var boardB = Guid.NewGuid();
        var userId = Guid.NewGuid();

        tracker.Join(boardA, "conn-a", userId, "user");
        tracker.Join(boardB, "conn-b", userId, "user");

        var eviction = tracker.EvictUser(boardA, userId);

        eviction.EvictedConnectionIds.Should().ContainSingle().Which.Should().Be("conn-a");
        tracker.IsConnectionJoinedBoard("conn-b", boardB).Should().BeTrue();
    }
}
