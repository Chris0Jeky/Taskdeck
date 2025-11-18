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
}
