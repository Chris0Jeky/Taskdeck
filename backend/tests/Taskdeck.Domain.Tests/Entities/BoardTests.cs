using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class BoardTests
{
    [Fact]
    public void Constructor_ShouldCreateBoard_WithValidData()
    {
        // Arrange & Act
        var board = new Board("Personal", "My personal tasks");

        // Assert
        board.Name.Should().Be("Personal");
        board.Description.Should().Be("My personal tasks");
        board.IsArchived.Should().BeFalse();
        board.Id.Should().NotBeEmpty();
        board.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenNameIsEmpty()
    {
        // Act
        var act = () => new Board("");

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Board name cannot be empty")
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenNameIsTooLong()
    {
        // Arrange
        var longName = new string('a', 101);

        // Act
        var act = () => new Board(longName);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Board name cannot exceed 100 characters")
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Update_ShouldUpdateName()
    {
        // Arrange
        var board = new Board("Personal");
        var originalUpdatedAt = board.UpdatedAt;

        // Act
        Thread.Sleep(10); // Ensure time difference
        board.Update(name: "Work");

        // Assert
        board.Name.Should().Be("Work");
        board.UpdatedAt.Should().BeAfter(originalUpdatedAt);
    }

    [Fact]
    public void Update_ShouldUpdateDescription()
    {
        // Arrange
        var board = new Board("Personal");

        // Act
        board.Update(description: "New description");

        // Assert
        board.Description.Should().Be("New description");
    }

    [Fact]
    public void Archive_ShouldSetIsArchivedToTrue()
    {
        // Arrange
        var board = new Board("Personal");

        // Act
        board.Archive();

        // Assert
        board.IsArchived.Should().BeTrue();
    }

    [Fact]
    public void Unarchive_ShouldSetIsArchivedToFalse()
    {
        // Arrange
        var board = new Board("Personal");
        board.Archive();

        // Act
        board.Unarchive();

        // Assert
        board.IsArchived.Should().BeFalse();
    }

    [Fact]
    public void Constructor_ShouldSetOwnerId_WhenProvided()
    {
        // Arrange
        var ownerId = Guid.NewGuid();

        // Act
        var board = new Board("Team", ownerId: ownerId);

        // Assert
        board.OwnerId.Should().Be(ownerId);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenOwnerIdIsEmpty()
    {
        // Act
        var act = () => new Board("Team", ownerId: Guid.Empty);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Owner ID cannot be empty")
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void TransferOwnership_ShouldSetOwnerId()
    {
        // Arrange
        var originalOwnerId = Guid.NewGuid();
        var newOwnerId = Guid.NewGuid();
        var board = new Board("Team", ownerId: originalOwnerId);

        // Act
        board.TransferOwnership(newOwnerId);

        // Assert
        board.OwnerId.Should().Be(newOwnerId);
    }

    [Fact]
    public void Constructor_ShouldSetDescriptionToNull_WhenNotProvided()
    {
        // Arrange & Act
        var board = new Board("Personal");

        // Assert
        board.Description.Should().BeNull();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenDescriptionTooLong()
    {
        // Arrange
        var longDescription = new string('a', 1001);

        // Act
        var act = () => new Board("Personal", longDescription);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Board description cannot exceed 1000 characters");
    }

    [Fact]
    public void Update_ShouldThrow_WhenDescriptionTooLong()
    {
        // Arrange
        var board = new Board("Personal");
        var longDescription = new string('a', 1001);

        // Act
        var act = () => board.Update(description: longDescription);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Board description cannot exceed 1000 characters");
    }

    [Fact]
    public void Update_ShouldNotChangeName_WhenNameIsNull()
    {
        // Arrange
        var board = new Board("Personal");

        // Act
        board.Update(name: null, description: "New description");

        // Assert
        board.Name.Should().Be("Personal");
    }

    [Fact]
    public void Update_ShouldNotChangeDescription_WhenDescriptionIsNull()
    {
        // Arrange
        var board = new Board("Personal", "Original Description");

        // Act
        board.Update(name: "Updated", description: null);

        // Assert
        board.Description.Should().Be("Original Description");
    }

    [Fact]
    public void TransferOwnership_ShouldThrow_WhenNewOwnerIdIsEmpty()
    {
        // Arrange
        var board = new Board("Team", ownerId: Guid.NewGuid());

        // Act
        var act = () => board.TransferOwnership(Guid.Empty);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("New owner ID cannot be empty")
            .Where(e => e.ErrorCode == ErrorCodes.ValidationError);
    }

    [Fact]
    public void Archive_ThenUnarchive_ShouldRestoreState()
    {
        // Arrange
        var board = new Board("Personal");
        board.IsArchived.Should().BeFalse();

        // Act
        board.Archive();
        board.IsArchived.Should().BeTrue();
        board.Unarchive();

        // Assert
        board.IsArchived.Should().BeFalse();
    }
}
