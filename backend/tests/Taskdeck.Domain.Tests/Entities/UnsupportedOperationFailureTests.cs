using FluentAssertions;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class UnsupportedOperationFailureTests
{
    [Fact]
    public void Constructor_ShouldCreateFailure_WithValidData()
    {
        var failure = new UnsupportedOperationFailure(
            "delete_board", "board", "Board deletion is not supported");

        failure.ActionType.Should().Be("delete_board");
        failure.TargetType.Should().Be("board");
        failure.Reason.Should().Be("Board deletion is not supported");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Constructor_ShouldThrow_WhenActionTypeIsBlank(string actionType)
    {
        var act = () => new UnsupportedOperationFailure(actionType, "card", "reason");

        act.Should().Throw<ArgumentException>()
            .WithMessage("ActionType cannot be empty.*");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Constructor_ShouldThrow_WhenTargetTypeIsBlank(string targetType)
    {
        var act = () => new UnsupportedOperationFailure("create", targetType, "reason");

        act.Should().Throw<ArgumentException>()
            .WithMessage("TargetType cannot be empty.*");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Constructor_ShouldThrow_WhenReasonIsBlank(string reason)
    {
        var act = () => new UnsupportedOperationFailure("create", "card", reason);

        act.Should().Throw<ArgumentException>()
            .WithMessage("Reason cannot be empty.*");
    }

    [Fact]
    public void Equals_SameValues_ShouldBeEqual()
    {
        var f1 = new UnsupportedOperationFailure("delete", "board", "not allowed");
        var f2 = new UnsupportedOperationFailure("delete", "board", "not allowed");

        f1.Should().Be(f2);
        f1.Equals(f2).Should().BeTrue();
        (f1.GetHashCode() == f2.GetHashCode()).Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentActionType_ShouldNotBeEqual()
    {
        var f1 = new UnsupportedOperationFailure("create", "board", "reason");
        var f2 = new UnsupportedOperationFailure("delete", "board", "reason");

        f1.Should().NotBe(f2);
    }

    [Fact]
    public void Equals_DifferentTargetType_ShouldNotBeEqual()
    {
        var f1 = new UnsupportedOperationFailure("delete", "board", "reason");
        var f2 = new UnsupportedOperationFailure("delete", "card", "reason");

        f1.Should().NotBe(f2);
    }

    [Fact]
    public void Equals_DifferentReason_ShouldNotBeEqual()
    {
        var f1 = new UnsupportedOperationFailure("delete", "board", "reason A");
        var f2 = new UnsupportedOperationFailure("delete", "board", "reason B");

        f1.Should().NotBe(f2);
    }

    [Fact]
    public void Equals_Null_ShouldNotBeEqual()
    {
        var failure = new UnsupportedOperationFailure("delete", "board", "reason");

        failure.Equals(null).Should().BeFalse();
    }

    [Fact]
    public void ToString_ShouldFormatCorrectly()
    {
        var failure = new UnsupportedOperationFailure(
            "merge", "column", "Column merge not supported");

        failure.ToString().Should().Be("Unsupported: merge on column - Column merge not supported");
    }
}
