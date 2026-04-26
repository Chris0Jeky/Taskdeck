using FluentAssertions;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class SideEffectRowTests
{
    [Fact]
    public void Constructor_ShouldCreateRow_WithValidData()
    {
        var row = new SideEffectRow("Cards", "Creates cards", SideEffectTone.Active);

        row.Key.Should().Be("Cards");
        row.Value.Should().Be("Creates cards");
        row.Tone.Should().Be(SideEffectTone.Active);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Constructor_ShouldThrow_WhenKeyIsBlank(string key)
    {
        var act = () => new SideEffectRow(key, "some value", SideEffectTone.Active);

        act.Should().Throw<ArgumentException>()
            .WithMessage("Side-effect key cannot be empty.*");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Constructor_ShouldThrow_WhenValueIsBlank(string value)
    {
        var act = () => new SideEffectRow("Cards", value, SideEffectTone.Active);

        act.Should().Throw<ArgumentException>()
            .WithMessage("Side-effect value cannot be empty.*");
    }

    [Fact]
    public void Constructor_ShouldAcceptPassiveTone()
    {
        var row = new SideEffectRow("Calendar", "Not yet integrated", SideEffectTone.Passive);

        row.Tone.Should().Be(SideEffectTone.Passive);
    }

    [Fact]
    public void Equals_SameValues_ShouldBeEqual()
    {
        var row1 = new SideEffectRow("Cards", "Creates cards", SideEffectTone.Active);
        var row2 = new SideEffectRow("Cards", "Creates cards", SideEffectTone.Active);

        row1.Should().Be(row2);
        row1.Equals(row2).Should().BeTrue();
        (row1.GetHashCode() == row2.GetHashCode()).Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentKey_ShouldNotBeEqual()
    {
        var row1 = new SideEffectRow("Cards", "Same value", SideEffectTone.Active);
        var row2 = new SideEffectRow("Subtasks", "Same value", SideEffectTone.Active);

        row1.Should().NotBe(row2);
    }

    [Fact]
    public void Equals_DifferentValue_ShouldNotBeEqual()
    {
        var row1 = new SideEffectRow("Cards", "Value A", SideEffectTone.Active);
        var row2 = new SideEffectRow("Cards", "Value B", SideEffectTone.Active);

        row1.Should().NotBe(row2);
    }

    [Fact]
    public void Equals_DifferentTone_ShouldNotBeEqual()
    {
        var row1 = new SideEffectRow("Cards", "Same value", SideEffectTone.Active);
        var row2 = new SideEffectRow("Cards", "Same value", SideEffectTone.Passive);

        row1.Should().NotBe(row2);
    }

    [Fact]
    public void Equals_Null_ShouldNotBeEqual()
    {
        var row = new SideEffectRow("Cards", "Value", SideEffectTone.Active);

        row.Equals(null).Should().BeFalse();
    }

    [Fact]
    public void ToString_ShouldFormatCorrectly()
    {
        var row = new SideEffectRow("Cards", "Creates cards", SideEffectTone.Active);

        row.ToString().Should().Be("[Active] Cards: Creates cards");
    }

    [Theory]
    [InlineData(SideEffectTone.Active)]
    [InlineData(SideEffectTone.Passive)]
    public void Constructor_ShouldAcceptAllToneValues(SideEffectTone tone)
    {
        var row = new SideEffectRow("Key", "Value", tone);

        row.Tone.Should().Be(tone);
    }
}
