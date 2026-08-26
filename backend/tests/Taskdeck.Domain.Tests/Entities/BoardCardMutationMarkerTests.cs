using FluentAssertions;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

/// <summary>
/// Pins the archive-race guard marker (ADR-0063, `#2115`, `#2123`): a card write must always move
/// something on the board row, must never move the concurrency token, and must never move the
/// user-visible <c>UpdatedAt</c> that the cached board list serves.
/// </summary>
public class BoardCardMutationMarkerTests
{
    [Fact]
    public void RecordCardMutation_ShouldNotChangeUpdatedAt()
    {
        var board = new Board("Personal");
        var updatedAtBefore = board.UpdatedAt;

        board.RecordCardMutation();

        board.UpdatedAt.Should().Be(updatedAtBefore,
            "UpdatedAt is served from the cached per-user board list, which no card write invalidates");
    }

    [Fact]
    public void RecordCardMutation_ShouldNotAdvanceConcurrencyToken()
    {
        var board = new Board("Personal");
        var tokenBefore = board.ConcurrencyToken;

        board.RecordCardMutation();

        board.ConcurrencyToken.Should().Be(tokenBefore,
            "advancing it would make independent card writes on one board invalidate each other");
    }

    [Fact]
    public void RecordCardMutation_ShouldAdvanceTheMarker()
    {
        var board = new Board("Personal");
        var markerBefore = board.CardMutationMarker;

        board.RecordCardMutation();

        board.CardMutationMarker.Should().Be(markerBefore + 1);
    }

    [Fact]
    public void RecordCardMutation_ShouldAdvanceTheMarker_OnEveryConsecutiveCall()
    {
        // The whole point of a counter over a re-stamped timestamp: back-to-back writes inside one
        // clock tick each move the value, so EF sees a modified row every time (`#2123`).
        var board = new Board("Personal");
        var markerBefore = board.CardMutationMarker;

        board.RecordCardMutation();
        board.RecordCardMutation();
        board.RecordCardMutation();

        board.CardMutationMarker.Should().Be(markerBefore + 3);
    }

    [Fact]
    public void NewBoard_ShouldStartWithAZeroMarker()
    {
        new Board("Personal").CardMutationMarker.Should().Be(0);
    }

    [Fact]
    public void BoardMutations_ShouldStillAdvanceConcurrencyTokenAndUpdatedAt()
    {
        // The guard only works because board mutations DO advance the token the card write reads.
        var board = new Board("Personal");

        var tokenAfterUpdate = MutateAndReturnToken(board, b => b.Update(name: "Work"));
        var tokenAfterArchive = MutateAndReturnToken(board, b => b.Archive());
        var tokenAfterUnarchive = MutateAndReturnToken(board, b => b.Unarchive());
        var tokenAfterTransfer = MutateAndReturnToken(board, b => b.TransferOwnership(Guid.NewGuid()));

        new[] { tokenAfterUpdate, tokenAfterArchive, tokenAfterUnarchive, tokenAfterTransfer }
            .Should().OnlyHaveUniqueItems();
        board.CardMutationMarker.Should().Be(0, "board mutations do not touch the card-write marker");
    }

    private static Guid MutateAndReturnToken(Board board, Action<Board> mutation)
    {
        var tokenBefore = board.ConcurrencyToken;
        mutation(board);
        board.ConcurrencyToken.Should().NotBe(tokenBefore);
        return board.ConcurrencyToken;
    }
}
