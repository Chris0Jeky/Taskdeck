using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Tests.PropertyBased;

/// <summary>
/// Property-based tests for Card entity invariants.
/// Replay: set Replay = "seed,size" on any [Property] to reproduce a failing case.
/// </summary>
public class CardPropertyTests
{
    private const int MaxTests = 200;

    private static readonly Guid TestBoardId = Guid.NewGuid();
    private static readonly Guid TestColumnId = Guid.NewGuid();

    [Property(MaxTest = MaxTests)]
    public Property ValidTitle_AlwaysCreatesCard()
    {
        return Prop.ForAll(
            ValidCardTitleArb(),
            title =>
            {
                var card = new Card(TestBoardId, TestColumnId, title);
                card.Title.Should().Be(title);
                card.BoardId.Should().Be(TestBoardId);
                card.ColumnId.Should().Be(TestColumnId);
                card.IsBlocked.Should().BeFalse();
                card.Position.Should().Be(0);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property EmptyOrWhitespaceTitle_AlwaysThrows()
    {
        return Prop.ForAll(
            Arb.From(Gen.Elements("", " ", "\t", "\n", "  \t\n  ")),
            title =>
            {
                var act = () => new Card(TestBoardId, TestColumnId, title);
                act.Should().Throw<DomainException>()
                    .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property TitleExceeding200Chars_AlwaysThrows()
    {
        return Prop.ForAll(
            Arb.From(Gen.Choose(201, 500).Select(len => new string('x', len))),
            longTitle =>
            {
                var act = () => new Card(TestBoardId, TestColumnId, longTitle);
                act.Should().Throw<DomainException>()
                    .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property DescriptionExceeding2000Chars_AlwaysThrows()
    {
        return Prop.ForAll(
            Arb.From(Gen.Choose(2001, 3000).Select(len => new string('d', len))),
            longDesc =>
            {
                var act = () => new Card(TestBoardId, TestColumnId, "Valid", longDesc);
                act.Should().Throw<DomainException>()
                    .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property NegativePosition_AlwaysThrows()
    {
        return Prop.ForAll(
            Arb.From(Gen.Choose(-1000, -1)),
            negPos =>
            {
                var card = new Card(TestBoardId, TestColumnId, "Valid");
                var act = () => card.SetPosition(negPos);
                act.Should().Throw<DomainException>()
                    .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property NonNegativePosition_AlwaysAccepted()
    {
        return Prop.ForAll(
            Arb.From(Gen.Choose(0, 10000)),
            pos =>
            {
                var card = new Card(TestBoardId, TestColumnId, "Valid");
                card.SetPosition(pos);
                card.Position.Should().Be(pos);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property BlockUnblock_CyclePreservesState()
    {
        return Prop.ForAll(
            Arb.From(Gen.Choose(1, 10)),
            cycles =>
            {
                var card = new Card(TestBoardId, TestColumnId, "Valid");
                for (int i = 0; i < cycles; i++)
                {
                    card.Block("reason");
                    card.IsBlocked.Should().BeTrue();
                    card.BlockReason.Should().Be("reason");

                    card.Unblock();
                    card.IsBlocked.Should().BeFalse();
                    card.BlockReason.Should().BeNull();
                }
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property Block_WithEmptyReason_AlwaysThrows()
    {
        return Prop.ForAll(
            Arb.From(Gen.Elements("", " ", "\t", "\n")),
            reason =>
            {
                var card = new Card(TestBoardId, TestColumnId, "Valid");
                var act = () => card.Block(reason);
                act.Should().Throw<DomainException>()
                    .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property MoveToColumn_UpdatesBothColumnAndPosition()
    {
        return Prop.ForAll(
            Arb.From(Gen.Fresh(() => Guid.NewGuid())),
            Arb.From(Gen.Choose(0, 1000)),
            (newColId, newPos) =>
            {
                var card = new Card(TestBoardId, TestColumnId, "Valid");
                card.MoveToColumn(newColId, newPos);
                card.ColumnId.Should().Be(newColId);
                card.Position.Should().Be(newPos);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property ExplicitCardId_RejectsEmptyGuid()
    {
        var act = () => new Card(Guid.Empty, TestBoardId, TestColumnId, "Valid");
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
        return true.ToProperty();
    }

    private static Arbitrary<string> ValidCardTitleArb()
    {
        var gen = Gen.Choose(1, 200)
            .SelectMany(len =>
                Gen.ArrayOf(Gen.Elements(
                    'a', 'b', 'c', 'A', 'B', 'C', '1', '2', '3', ' ', '-', '_'), len)
                .Select(chars => new string(chars)))
            .Where(s => !string.IsNullOrWhiteSpace(s));
        return Arb.From(gen);
    }
}
