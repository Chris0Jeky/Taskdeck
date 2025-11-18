using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class CardTests
{
    private readonly Guid _boardId = Guid.NewGuid();
    private readonly Guid _columnId = Guid.NewGuid();

    [Fact]
    public void Constructor_ShouldCreateCard_WithValidData()
    {
        // Arrange & Act
        var dueDate = DateTimeOffset.UtcNow.AddDays(7);
        var card = new Card(_boardId, _columnId, "Fix bug", "Description here", dueDate, position: 0);

        // Assert
        card.Title.Should().Be("Fix bug");
        card.Description.Should().Be("Description here");
        card.DueDate.Should().Be(dueDate);
        card.Position.Should().Be(0);
        card.IsBlocked.Should().BeFalse();
        card.BoardId.Should().Be(_boardId);
        card.ColumnId.Should().Be(_columnId);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenTitleIsEmpty()
    {
        // Act
        var act = () => new Card(_boardId, _columnId, "");

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Card title cannot be empty");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenTitleIsTooLong()
    {
        // Arrange
        var longTitle = new string('a', 201);

        // Act
        var act = () => new Card(_boardId, _columnId, longTitle);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Card title cannot exceed 200 characters");
    }

    [Fact]
    public void Update_ShouldUpdateFields()
    {
        // Arrange
        var card = new Card(_boardId, _columnId, "Original");
        var newDueDate = DateTimeOffset.UtcNow.AddDays(5);

        // Act
        card.Update(title: "Updated", description: "New desc", dueDate: newDueDate);

        // Assert
        card.Title.Should().Be("Updated");
        card.Description.Should().Be("New desc");
        card.DueDate.Should().Be(newDueDate);
    }

    [Fact]
    public void Block_ShouldSetBlockedState()
    {
        // Arrange
        var card = new Card(_boardId, _columnId, "Task");

        // Act
        card.Block("Waiting for API");

        // Assert
        card.IsBlocked.Should().BeTrue();
        card.BlockReason.Should().Be("Waiting for API");
    }

    [Fact]
    public void Block_ShouldThrow_WhenReasonIsEmpty()
    {
        // Arrange
        var card = new Card(_boardId, _columnId, "Task");

        // Act
        var act = () => card.Block("");

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Block reason cannot be empty");
    }

    [Fact]
    public void Unblock_ShouldClearBlockedState()
    {
        // Arrange
        var card = new Card(_boardId, _columnId, "Task");
        card.Block("Waiting");

        // Act
        card.Unblock();

        // Assert
        card.IsBlocked.Should().BeFalse();
        card.BlockReason.Should().BeNull();
    }

    [Fact]
    public void MoveToColumn_ShouldUpdateColumnAndPosition()
    {
        // Arrange
        var card = new Card(_boardId, _columnId, "Task");
        var newColumnId = Guid.NewGuid();

        // Act
        card.MoveToColumn(newColumnId, 2);

        // Assert
        card.ColumnId.Should().Be(newColumnId);
        card.Position.Should().Be(2);
    }

    [Fact]
    public void SetPosition_ShouldUpdatePosition()
    {
        // Arrange
        var card = new Card(_boardId, _columnId, "Task");

        // Act
        card.SetPosition(3);

        // Assert
        card.Position.Should().Be(3);
    }

    [Fact]
    public void SetPosition_ShouldThrow_WhenPositionIsNegative()
    {
        // Arrange
        var card = new Card(_boardId, _columnId, "Task");

        // Act
        var act = () => card.SetPosition(-1);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Position cannot be negative");
    }
}
