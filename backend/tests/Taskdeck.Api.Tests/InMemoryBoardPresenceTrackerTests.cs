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
}
