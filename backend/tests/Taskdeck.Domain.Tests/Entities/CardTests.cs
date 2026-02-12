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

    [Fact]
    public void Constructor_ShouldSetDefaultDescription_WhenNotProvided()
    {
        // Arrange & Act
        var card = new Card(_boardId, _columnId, "Task");

        // Assert
        card.Description.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenDescriptionTooLong()
    {
        // Arrange
        var longDescription = new string('a', 2001);

        // Act
        var act = () => new Card(_boardId, _columnId, "Task", longDescription);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Card description cannot exceed 2000 characters");
    }

    [Fact]
    public void Update_ShouldThrow_WhenDescriptionTooLong()
    {
        // Arrange
        var card = new Card(_boardId, _columnId, "Task");
        var longDescription = new string('a', 2001);

        // Act
        var act = () => card.Update(description: longDescription);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Card description cannot exceed 2000 characters");
    }

    [Fact]
    public void Update_ShouldNotChangeTitle_WhenTitleIsNull()
    {
        // Arrange
        var card = new Card(_boardId, _columnId, "Original Title");

        // Act
        card.Update(title: null, description: "New desc");

        // Assert
        card.Title.Should().Be("Original Title");
    }

    [Fact]
    public void Update_ShouldNotChangeDescription_WhenDescriptionIsNull()
    {
        // Arrange
        var card = new Card(_boardId, _columnId, "Task", "Original Description");

        // Act
        card.Update(title: "New Title", description: null);

        // Assert
        card.Description.Should().Be("Original Description");
    }

    [Fact]
    public void MoveToColumn_ShouldThrow_WhenPositionIsNegative()
    {
        // Arrange
        var card = new Card(_boardId, _columnId, "Task");
        var newColumnId = Guid.NewGuid();

        // Act
        var act = () => card.MoveToColumn(newColumnId, -1);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Position cannot be negative");
    }

    [Fact]
    public void AddLabel_ShouldThrow_WhenDuplicateLabelAdded()
    {
        // Arrange
        var card = new Card(_boardId, _columnId, "Task");
        var labelId = Guid.NewGuid();
        var cardLabel = new CardLabel(card.Id, labelId);
        card.AddLabel(cardLabel);

        // Act
        var duplicateLabel = new CardLabel(card.Id, labelId);
        var act = () => card.AddLabel(duplicateLabel);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Label is already assigned to this card");
    }

    [Fact]
    public void AddLabel_ShouldAddLabel_WhenLabelNotAlreadyAdded()
    {
        // Arrange
        var card = new Card(_boardId, _columnId, "Task");
        var cardLabel = new CardLabel(card.Id, Guid.NewGuid());

        // Act
        card.AddLabel(cardLabel);

        // Assert
        card.CardLabels.Should().ContainSingle()
            .Which.Should().Be(cardLabel);
    }

    [Fact]
    public void RemoveLabel_ShouldRemoveLabel()
    {
        // Arrange
        var card = new Card(_boardId, _columnId, "Task");
        var cardLabel = new CardLabel(card.Id, Guid.NewGuid());
        card.AddLabel(cardLabel);

        // Act
        card.RemoveLabel(cardLabel);

        // Assert
        card.CardLabels.Should().BeEmpty();
    }

    [Fact]
    public void ClearLabels_ShouldRemoveAllLabels()
    {
        // Arrange
        var card = new Card(_boardId, _columnId, "Task");
        card.AddLabel(new CardLabel(card.Id, Guid.NewGuid()));
        card.AddLabel(new CardLabel(card.Id, Guid.NewGuid()));

        // Act
        card.ClearLabels();

        // Assert
        card.CardLabels.Should().BeEmpty();
    }
}
