using FluentAssertions;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class ConflictRowTests
{
    [Theory]
    [InlineData(ConflictTone.Warn)]
    [InlineData(ConflictTone.Info)]
    [InlineData(ConflictTone.Ok)]
    public void Constructor_ShouldCreateRow_WithValidData(ConflictTone tone)
    {
        var row = new ConflictRow(tone, "test-key", "test value");

        row.Tone.Should().Be(tone);
        row.Key.Should().Be("test-key");
        row.Value.Should().Be("test value");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Constructor_ShouldThrow_WhenKeyIsBlank(string key)
    {
        var act = () => new ConflictRow(ConflictTone.Warn, key, "value");

        act.Should().Throw<ArgumentException>()
            .WithMessage("Conflict row key cannot be empty.*");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Constructor_ShouldThrow_WhenValueIsBlank(string value)
    {
        var act = () => new ConflictRow(ConflictTone.Info, "key", value);

        act.Should().Throw<ArgumentException>()
            .WithMessage("Conflict row value cannot be empty.*");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenToneIsInvalid()
    {
        var act = () => new ConflictRow((ConflictTone)99, "key", "value");

        act.Should().Throw<ArgumentException>()
            .WithMessage("Invalid conflict tone*");
    }

    [Fact]
    public void Equals_SameValues_ShouldBeEqual()
    {
        var row1 = new ConflictRow(ConflictTone.Warn, "stale-data", "Card was modified");
        var row2 = new ConflictRow(ConflictTone.Warn, "stale-data", "Card was modified");

        row1.Should().Be(row2);
        row1.Equals(row2).Should().BeTrue();
        (row1.GetHashCode() == row2.GetHashCode()).Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentTone_ShouldNotBeEqual()
    {
        var row1 = new ConflictRow(ConflictTone.Warn, "key", "value");
        var row2 = new ConflictRow(ConflictTone.Info, "key", "value");

        row1.Should().NotBe(row2);
    }

    [Fact]
    public void Equals_DifferentKey_ShouldNotBeEqual()
    {
        var row1 = new ConflictRow(ConflictTone.Ok, "key-a", "value");
        var row2 = new ConflictRow(ConflictTone.Ok, "key-b", "value");

        row1.Should().NotBe(row2);
    }

    [Fact]
    public void Equals_DifferentValue_ShouldNotBeEqual()
    {
        var row1 = new ConflictRow(ConflictTone.Info, "key", "value A");
        var row2 = new ConflictRow(ConflictTone.Info, "key", "value B");

        row1.Should().NotBe(row2);
    }

    [Fact]
    public void Equals_Null_ShouldNotBeEqual()
    {
        var row = new ConflictRow(ConflictTone.Ok, "key", "value");

        row.Equals(null).Should().BeFalse();
    }

    [Fact]
    public void ToString_ShouldFormatCorrectly()
    {
        var row = new ConflictRow(ConflictTone.Warn, "stale-data", "Card was modified");

        row.ToString().Should().Be("[Warn] stale-data: Card was modified");
    }

    [Theory]
    [InlineData(ConflictTone.Warn)]
    [InlineData(ConflictTone.Info)]
    [InlineData(ConflictTone.Ok)]
    public void Constructor_ShouldAcceptAllTones(ConflictTone tone)
    {
        var row = new ConflictRow(tone, "test", "test");

        row.Tone.Should().Be(tone);
    }
}
