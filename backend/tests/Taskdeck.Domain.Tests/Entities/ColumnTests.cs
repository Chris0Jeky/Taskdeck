using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class ColumnTests
{
    private readonly Guid _boardId = Guid.NewGuid();

    [Fact]
    public void Constructor_ShouldCreateColumn_WithValidData()
    {
        // Arrange & Act
        var column = new Column(_boardId, "To Do", 0, wipLimit: 5);

        // Assert
        column.Name.Should().Be("To Do");
        column.Position.Should().Be(0);
        column.WipLimit.Should().Be(5);
        column.BoardId.Should().Be(_boardId);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenNameIsEmpty()
    {
        // Act
        var act = () => new Column(_boardId, "", 0);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Column name cannot be empty");
    }

    [Fact]
    public void SetWipLimit_ShouldThrow_WhenLimitIsZero()
    {
        // Arrange
        var column = new Column(_boardId, "To Do", 0);

        // Act
        var act = () => column.SetWipLimit(0);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("WIP limit must be greater than 0");
    }

    [Fact]
    public void SetWipLimit_ShouldThrow_WhenLimitIsNegative()
    {
        // Arrange
        var column = new Column(_boardId, "To Do", 0);

        // Act
        var act = () => column.SetWipLimit(-1);

        // Assert
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void SetWipLimit_ShouldAllowNull()
    {
        // Arrange
        var column = new Column(_boardId, "To Do", 0, wipLimit: 5);

        // Act
        column.SetWipLimit(null);

        // Assert
        column.WipLimit.Should().BeNull();
    }

    [Fact]
    public void SetPosition_ShouldUpdatePosition()
    {
        // Arrange
        var column = new Column(_boardId, "To Do", 0);

        // Act
        column.SetPosition(2);

        // Assert
        column.Position.Should().Be(2);
    }

    [Fact]
    public void SetPosition_ShouldThrow_WhenPositionIsNegative()
    {
        // Arrange
        var column = new Column(_boardId, "To Do", 0);

        // Act
        var act = () => column.SetPosition(-1);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Position cannot be negative");
    }

    [Fact]
    public void Update_ShouldUpdateAllFields()
    {
        // Arrange
        var column = new Column(_boardId, "To Do", 0, wipLimit: 5);

        // Act
        column.Update(name: "Doing", wipLimit: 3, position: 1);

        // Assert
        column.Name.Should().Be("Doing");
        column.WipLimit.Should().Be(3);
        column.Position.Should().Be(1);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenNameTooLong()
    {
        // Arrange
        var longName = new string('a', 51);

        // Act
        var act = () => new Column(_boardId, longName, 0);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Column name cannot exceed 50 characters");
    }

    [Fact]
    public void Update_ShouldNotChangeName_WhenNameIsNull()
    {
        // Arrange
        var column = new Column(_boardId, "To Do", 0);

        // Act
        column.Update(name: null, wipLimit: 3);

        // Assert
        column.Name.Should().Be("To Do");
    }

    [Fact]
    public void WouldExceedWipLimitIfAdded_ShouldReturnFalse_WhenNoWipLimit()
    {
        // Arrange
        var column = new Column(_boardId, "To Do", 0);

        // Act
        var result = column.WouldExceedWipLimitIfAdded();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void WouldExceedWipLimitIfAdded_ShouldReturnFalse_WhenUnderLimit()
    {
        // Arrange
        var column = new Column(_boardId, "To Do", 0, wipLimit: 3);
        column.AddCard(new Card(_boardId, column.Id, "Card 1"));

        // Act
        var result = column.WouldExceedWipLimitIfAdded();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void WouldExceedWipLimitIfAdded_ShouldReturnTrue_WhenAtLimit()
    {
        // Arrange
        var column = new Column(_boardId, "To Do", 0, wipLimit: 2);
        column.AddCard(new Card(_boardId, column.Id, "Card 1"));
        column.AddCard(new Card(_boardId, column.Id, "Card 2"));

        // Act
        var result = column.WouldExceedWipLimitIfAdded();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsWipLimitExceeded_ShouldReturnFalse_WhenNoWipLimit()
    {
        // Arrange
        var column = new Column(_boardId, "To Do", 0);

        // Act
        var result = column.IsWipLimitExceeded();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsWipLimitExceeded_ShouldReturnFalse_WhenUnderOrAtLimit()
    {
        // Arrange
        var column = new Column(_boardId, "To Do", 0, wipLimit: 2);
        column.AddCard(new Card(_boardId, column.Id, "Card 1"));
        column.AddCard(new Card(_boardId, column.Id, "Card 2"));

        // Act
        var result = column.IsWipLimitExceeded();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsWipLimitExceeded_ShouldReturnTrue_WhenOverLimit()
    {
        // Arrange
        var column = new Column(_boardId, "To Do", 0, wipLimit: 1);
        column.AddCard(new Card(_boardId, column.Id, "Card 1"));
        column.AddCard(new Card(_boardId, column.Id, "Card 2"));

        // Act
        var result = column.IsWipLimitExceeded();

        // Assert
        result.Should().BeTrue();
    }
}
