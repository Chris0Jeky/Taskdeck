using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Tests.PropertyBased;

/// <summary>
/// Property-based tests for Board entity invariants.
/// FsCheck generates random inputs to verify domain constraints hold for all valid/invalid values.
/// Replay: set Replay = "seed,size" on any [Property] to reproduce a failing case deterministically.
/// </summary>
public class BoardPropertyTests
{
    // Runtime budget: MaxTest caps total generated cases to keep CI fast (default 100).
    private const int MaxTests = 200;

    [Property(MaxTest = MaxTests)]
    public Property ValidName_AlwaysCreatesBoard()
    {
        return Prop.ForAll(
            ValidBoardNameArb(),
            name =>
            {
                var board = new Board(name);
                board.Name.Should().Be(name);
                board.IsArchived.Should().BeFalse();
                board.Id.Should().NotBeEmpty();
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property EmptyOrWhitespaceName_AlwaysThrows()
    {
        return Prop.ForAll(
            Arb.From(Gen.Elements("", " ", "\t", "\n", "  \t\n  ")),
            name =>
            {
                var act = () => new Board(name);
                act.Should().Throw<DomainException>()
                    .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property NameExceeding100Chars_AlwaysThrows()
    {
        return Prop.ForAll(
            Arb.From(Gen.Choose(101, 500).Select(len => new string('x', len))),
            longName =>
            {
                var act = () => new Board(longName);
                act.Should().Throw<DomainException>()
                    .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property NameAtExactly100Chars_Succeeds()
    {
        var name100 = new string('a', 100);
        var board = new Board(name100);
        return (board.Name == name100).ToProperty();
    }

    [Property(MaxTest = MaxTests)]
    public Property DescriptionExceeding1000Chars_AlwaysThrows()
    {
        return Prop.ForAll(
            Arb.From(Gen.Choose(1001, 2000).Select(len => new string('d', len))),
            longDesc =>
            {
                var act = () => new Board("Valid", longDesc);
                act.Should().Throw<DomainException>()
                    .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property ValidDescription_AlwaysAccepted()
    {
        return Prop.ForAll(
            Arb.From(Gen.Choose(0, 1000).Select(len => new string('d', len))),
            desc =>
            {
                var board = new Board("Valid", desc);
                board.Description.Should().Be(desc);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property ArchiveUnarchive_IsIdempotent()
    {
        return Prop.ForAll(
            Arb.From(Gen.Choose(1, 10)),
            toggleCount =>
            {
                var board = new Board("Test");
                for (int i = 0; i < toggleCount; i++)
                {
                    board.Archive();
                    board.IsArchived.Should().BeTrue();
                    board.Unarchive();
                    board.IsArchived.Should().BeFalse();
                }
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property TransferOwnership_RejectsEmptyGuid()
    {
        var board = new Board("Test");
        var act = () => board.TransferOwnership(Guid.Empty);
        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
        return true.ToProperty();
    }

    [Property(MaxTest = MaxTests)]
    public Property TransferOwnership_AcceptsAnyNonEmptyGuid()
    {
        return Prop.ForAll(
            Arb.From(Gen.Fresh(() => Guid.NewGuid())),
            newOwnerId =>
            {
                var board = new Board("Test", ownerId: Guid.NewGuid());
                board.TransferOwnership(newOwnerId);
                board.OwnerId.Should().Be(newOwnerId);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property Update_PreservesUnchangedFields()
    {
        return Prop.ForAll(
            ValidBoardNameArb(),
            name =>
            {
                var board = new Board("Original", "Desc");
                board.Update(name: name);
                board.Name.Should().Be(name);
                board.Description.Should().Be("Desc"); // unchanged
            });
    }

    /// <summary>
    /// Custom Arbitrary that generates valid board names: 1-100 non-whitespace-only chars.
    /// </summary>
    private static Arbitrary<string> ValidBoardNameArb()
    {
        var gen = Gen.Choose(1, 100)
            .SelectMany(len =>
                Gen.ArrayOf(len, Gen.Elements(
                    'a', 'b', 'c', 'A', 'B', 'C', '1', '2', '3', ' ', '-', '_'))
                .Select(chars => new string(chars)))
            .Where(s => !string.IsNullOrWhiteSpace(s));
        return Arb.From(gen);
    }
}
