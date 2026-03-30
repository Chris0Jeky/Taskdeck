using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Tests.PropertyBased;

/// <summary>
/// Property-based tests for Label entity invariants.
/// Replay: set Replay = "seed,size" on any [Property] to reproduce a failing case.
/// </summary>
public class LabelPropertyTests
{
    private const int MaxTests = 200;

    private static readonly Guid TestBoardId = Guid.NewGuid();

    [Property(MaxTest = MaxTests)]
    public Property ValidHexColor_AlwaysAccepted()
    {
        return Prop.ForAll(
            ValidHexColorArb(),
            color =>
            {
                var label = new Label(TestBoardId, "Valid", color);
                label.ColorHex.Should().Be(color.ToUpperInvariant());
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property InvalidHexColor_AlwaysThrows()
    {
        return Prop.ForAll(
            InvalidHexColorArb(),
            color =>
            {
                var act = () => new Label(TestBoardId, "Valid", color);
                act.Should().Throw<DomainException>()
                    .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property ValidName_AlwaysCreatesLabel()
    {
        return Prop.ForAll(
            ValidLabelNameArb(),
            name =>
            {
                var label = new Label(TestBoardId, name, "#FF0000");
                label.Name.Should().Be(name);
                label.BoardId.Should().Be(TestBoardId);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property EmptyOrWhitespaceName_AlwaysThrows()
    {
        return Prop.ForAll(
            Arb.From(Gen.Elements("", " ", "\t", "\n")),
            name =>
            {
                var act = () => new Label(TestBoardId, name, "#FF0000");
                act.Should().Throw<DomainException>()
                    .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property NameExceeding30Chars_AlwaysThrows()
    {
        return Prop.ForAll(
            Arb.From(Gen.Choose(31, 100).Select(len => new string('x', len))),
            longName =>
            {
                var act = () => new Label(TestBoardId, longName, "#FF0000");
                act.Should().Throw<DomainException>()
                    .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property NameAtExactly30Chars_Succeeds()
    {
        var name30 = new string('a', 30);
        var label = new Label(TestBoardId, name30, "#FF0000");
        return (label.Name == name30).ToProperty();
    }

    [Property(MaxTest = MaxTests)]
    public Property ColorIsAlwaysUppercased()
    {
        return Prop.ForAll(
            ValidHexColorArb(),
            color =>
            {
                var label = new Label(TestBoardId, "Test", color);
                label.ColorHex.Should().Be(label.ColorHex.ToUpperInvariant());
            });
    }

    [Property(MaxTest = MaxTests)]
    public Property Update_PreservesUnchangedFields()
    {
        return Prop.ForAll(
            ValidLabelNameArb(),
            name =>
            {
                var label = new Label(TestBoardId, "Original", "#FF0000");
                label.Update(name: name);
                label.Name.Should().Be(name);
                label.ColorHex.Should().Be("#FF0000"); // unchanged
            });
    }

    /// <summary>
    /// Generates valid hex colors in format #RRGGBB with mixed case.
    /// </summary>
    private static Arbitrary<string> ValidHexColorArb()
    {
        var hexChars = "0123456789abcdefABCDEF".ToCharArray();
        var gen = Gen.ArrayOf(6, Gen.Elements(hexChars))
            .Select(chars => "#" + new string(chars));
        return Arb.From(gen);
    }

    /// <summary>
    /// Generates strings that are NOT valid hex colors.
    /// </summary>
    private static Arbitrary<string> InvalidHexColorArb()
    {
        return Arb.From(Gen.OneOf(
            // Missing hash
            Gen.Constant("FF0000"),
            // Too short
            Gen.Constant("#FFF"),
            // Too long
            Gen.Constant("#FF00001"),
            // Invalid chars
            Gen.Constant("#GGGGGG"),
            Gen.Constant("#ZZZZZZ"),
            // Empty
            Gen.Constant(""),
            Gen.Constant(" "),
            // Random non-hex strings
            Gen.Elements("red", "blue", "rgb(0,0,0)", "#12345G", "##FF0000")
        ));
    }

    private static Arbitrary<string> ValidLabelNameArb()
    {
        var gen = Gen.Choose(1, 30)
            .SelectMany(len =>
                Gen.ArrayOf(len, Gen.Elements(
                    'a', 'b', 'c', 'A', 'B', 'C', '1', '2', '3', ' ', '-', '_'))
                .Select(chars => new string(chars)))
            .Where(s => !string.IsNullOrWhiteSpace(s));
        return Arb.From(gen);
    }
}
