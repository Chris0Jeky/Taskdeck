using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Tests.PropertyBased;

/// <summary>
/// Property-based tests for Column entity invariants.
/// Replay: set Replay = "seed,size" on any [Property] to reproduce a failing case.
/// </summary>
public class ColumnPropertyTests
{
    private const int MaxTests = 200;

    private static readonly Guid TestBoardId = Guid.NewGuid();

    [Property(MaxTest = MaxTests)]
    public Property ValidName_AlwaysCreatesColumn()
    {
        return Prop.ForAll(
            ValidColumnNameArb(),
            name =>
            {
                var column = new Column(TestBoardId, name, 0);
                column.Name.Should().Be(name);
                column.BoardId.Should().Be(TestBoardId);
                column.Position.Should().Be(0);
                column.WipLimit.Should().BeNull();
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property EmptyOrWhitespaceName_AlwaysThrows()
    {
        return Prop.ForAll(
            Arb.From(Gen.Elements("", " ", "\t", "\n")),
            name =>
            {
                var act = () => new Column(TestBoardId, name, 0);
                act.Should().Throw<DomainException>()
                    .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property NameExceeding50Chars_AlwaysThrows()
    {
        return Prop.ForAll(
            Arb.From(Gen.Choose(51, 200).Select(len => new string('x', len))),
            longName =>
            {
                var act = () => new Column(TestBoardId, longName, 0);
                act.Should().Throw<DomainException>()
                    .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property NameAtExactly50Chars_Succeeds()
    {
        var name50 = new string('a', 50);
        var column = new Column(TestBoardId, name50, 0);
        return (column.Name == name50).ToProperty();
    }

    [Property(MaxTest = MaxTests)]
    public Property PositiveWipLimit_AlwaysAccepted()
    {
        return Prop.ForAll(
            Arb.From(Gen.Choose(1, 10000)),
            wipLimit =>
            {
                var column = new Column(TestBoardId, "Valid", 0, wipLimit);
                column.WipLimit.Should().Be(wipLimit);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property ZeroOrNegativeWipLimit_AlwaysThrows()
    {
        return Prop.ForAll(
            Arb.From(Gen.Choose(-100, 0)),
            wipLimit =>
            {
                var act = () => new Column(TestBoardId, "Valid", 0, wipLimit);
                act.Should().Throw<DomainException>()
                    .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property NullWipLimit_AlwaysAccepted()
    {
        var column = new Column(TestBoardId, "Valid", 0, null);
        column.WipLimit.Should().BeNull();
        return true.ToProperty();
    }

    [Property(MaxTest = MaxTests)]
    public Property NegativePosition_AlwaysThrows()
    {
        return Prop.ForAll(
            Arb.From(Gen.Choose(-1000, -1)),
            negPos =>
            {
                var column = new Column(TestBoardId, "Valid", 0);
                var act = () => column.SetPosition(negPos);
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
                var column = new Column(TestBoardId, "Valid", 0);
                column.SetPosition(pos);
                column.Position.Should().Be(pos);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property SetWipLimit_ThenClear_ResetsToNull()
    {
        return Prop.ForAll(
            Arb.From(Gen.Choose(1, 100)),
            wipLimit =>
            {
                var column = new Column(TestBoardId, "Valid", 0, wipLimit);
                column.WipLimit.Should().Be(wipLimit);
                column.SetWipLimit(null);
                column.WipLimit.Should().BeNull();
            });
    }

    private static Arbitrary<string> ValidColumnNameArb()
    {
        var gen = Gen.Choose(1, 50)
            .SelectMany(len =>
                Gen.ArrayOf(Gen.Elements(
                    'a', 'b', 'c', 'A', 'B', 'C', '1', '2', '3', ' ', '-', '_'), len)
                .Select(chars => new string(chars)))
            .Where(s => !string.IsNullOrWhiteSpace(s));
        return Arb.From(gen);
    }
}
