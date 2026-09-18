using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class CardHierarchyTests
{
    [Fact]
    public void ThreeLinksAcceptsEveryTypeAndFourLinksRejects()
    {
        var cards = MakeCards(5);
        cards[0].SetWorkItemType(CardWorkItemType.Spike);
        cards[1].SetWorkItemType(CardWorkItemType.Epic);
        for (var i = 1; i < 4; i++) cards[i].SetParent(cards[i - 1].Id);
        CardHierarchy.Validate(cards);
        cards[4].SetParent(cards[3].Id);
        FluentActions.Invoking(() => CardHierarchy.Validate(cards)).Should().Throw<DomainException>();
    }

    [Fact]
    public void MovedSubtreeIncludesArchivedDescendants()
    {
        var cards = MakeCards(5);
        cards[1].SetParent(cards[0].Id);
        cards[2].SetParent(cards[1].Id);
        cards[4].SetParent(cards[3].Id);
        cards[2].Archive();
        FluentActions.Invoking(() => CardHierarchy.ValidateParent(cards, cards[0].Id, cards[4].Id))
            .Should().Throw<DomainException>();
        cards[0].ParentCardId.Should().BeNull();
    }

    [Fact]
    public void RejectsSelfCyclesMissingAndCrossBoardParents()
    {
        var cards = MakeCards(3);
        cards[1].SetParent(cards[0].Id);
        FluentActions.Invoking(() => CardHierarchy.ValidateParent(cards, cards[0].Id, cards[1].Id)).Should().Throw<DomainException>();
        FluentActions.Invoking(() => CardHierarchy.ValidateParent(cards, cards[0].Id, cards[0].Id)).Should().Throw<DomainException>();
        FluentActions.Invoking(() => CardHierarchy.ValidateParent(cards, cards[0].Id, Guid.NewGuid())).Should().Throw<DomainException>();
        var other = new Card(Guid.NewGuid(), Guid.NewGuid(), "Other");
        FluentActions.Invoking(() => CardHierarchy.ValidateParent(cards.Append(other), cards[0].Id, other.Id)).Should().Throw<DomainException>();
    }

    [Fact]
    public void HierarchyMarkerChangesTokenWithoutChangingBoardTimestamp()
    {
        var board = new Board("Board");
        var timestamp = board.UpdatedAt;
        var token = board.ConcurrencyToken;
        board.RecordHierarchyMutation();
        board.ConcurrencyToken.Should().NotBe(token);
        board.UpdatedAt.Should().Be(timestamp);
    }

    private static Card[] MakeCards(int count)
    {
        var boardId = Guid.NewGuid();
        var columnId = Guid.NewGuid();
        return Enumerable.Range(0, count).Select(i => new Card(boardId, columnId, $"Card {i}")).ToArray();
    }
}
