using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class CardHistoryRowTests
{
    [Fact]
    public void Constructor_ShouldCreateRow_WithValidData()
    {
        var row = new CardHistoryRow("#001", "Card created", "11:42", CardHistoryStatus.Past);

        row.Serial.Should().Be("#001");
        row.Event.Should().Be("Card created");
        row.Age.Should().Be("11:42");
        row.Status.Should().Be(CardHistoryStatus.Past);
    }

    [Theory]
    [InlineData(CardHistoryStatus.Pending)]
    [InlineData(CardHistoryStatus.Applied)]
    [InlineData(CardHistoryStatus.Past)]
    public void Constructor_ShouldAcceptAllStatuses(CardHistoryStatus status)
    {
        var row = new CardHistoryRow("#001", "Event", "11:42", status);

        row.Status.Should().Be(status);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Constructor_ShouldThrow_WhenSerialIsBlank(string serial)
    {
        var act = () => new CardHistoryRow(serial, "Event", "11:42", CardHistoryStatus.Past);

        act.Should().Throw<ArgumentException>()
            .WithMessage("Serial cannot be empty.*");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_ShouldThrow_WhenEventIsBlank(string @event)
    {
        var act = () => new CardHistoryRow("#001", @event, "11:42", CardHistoryStatus.Past);

        act.Should().Throw<ArgumentException>()
            .WithMessage("Event cannot be empty.*");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_ShouldThrow_WhenAgeIsBlank(string age)
    {
        var act = () => new CardHistoryRow("#001", "Event", age, CardHistoryStatus.Past);

        act.Should().Throw<ArgumentException>()
            .WithMessage("Age cannot be empty.*");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenStatusIsInvalid()
    {
        var act = () => new CardHistoryRow("#001", "Event", "11:42", (CardHistoryStatus)999);

        act.Should().Throw<ArgumentException>()
            .WithMessage("Invalid CardHistoryStatus value.*");
    }

    [Fact]
    public void Equals_SameValues_ShouldBeEqual()
    {
        var row1 = new CardHistoryRow("#001", "Card created", "11:42", CardHistoryStatus.Past);
        var row2 = new CardHistoryRow("#001", "Card created", "11:42", CardHistoryStatus.Past);

        row1.Should().Be(row2);
        row1.Equals(row2).Should().BeTrue();
        (row1.GetHashCode() == row2.GetHashCode()).Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentSerial_ShouldNotBeEqual()
    {
        var row1 = new CardHistoryRow("#001", "Card created", "11:42", CardHistoryStatus.Past);
        var row2 = new CardHistoryRow("#002", "Card created", "11:42", CardHistoryStatus.Past);

        row1.Should().NotBe(row2);
    }

    [Fact]
    public void Equals_DifferentStatus_ShouldNotBeEqual()
    {
        var row1 = new CardHistoryRow("#001", "Card created", "11:42", CardHistoryStatus.Past);
        var row2 = new CardHistoryRow("#001", "Card created", "11:42", CardHistoryStatus.Pending);

        row1.Should().NotBe(row2);
    }

    [Fact]
    public void Equals_Null_ShouldNotBeEqual()
    {
        var row = new CardHistoryRow("#001", "Event", "11:42", CardHistoryStatus.Past);

        row.Equals(null).Should().BeFalse();
    }

    [Fact]
    public void ToString_ShouldFormatCorrectly()
    {
        var row = new CardHistoryRow("#003", "Card moved", "yest 16:04", CardHistoryStatus.Applied);

        row.ToString().Should().Be("#003 Card moved (yest 16:04) [Applied]");
    }
}
