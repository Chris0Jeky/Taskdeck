using FluentAssertions;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Domain.Tests.Entities;

public class LabelTests
{
    private readonly Guid _boardId = Guid.NewGuid();

    [Fact]
    public void Constructor_ShouldCreateLabel_WithValidData()
    {
        // Arrange & Act
        var label = new Label(_boardId, "Bug", "#EF4444");

        // Assert
        label.Name.Should().Be("Bug");
        label.ColorHex.Should().Be("#EF4444");
        label.BoardId.Should().Be(_boardId);
    }

    [Fact]
    public void Constructor_ShouldNormalizeColorToUpperCase()
    {
        // Arrange & Act
        var label = new Label(_boardId, "Bug", "#ef4444");

        // Assert
        label.ColorHex.Should().Be("#EF4444");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenNameIsEmpty()
    {
        // Act
        var act = () => new Label(_boardId, "", "#EF4444");

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Label name cannot be empty");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenNameIsTooLong()
    {
        // Arrange
        var longName = new string('a', 31);

        // Act
        var act = () => new Label(_boardId, longName, "#EF4444");

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Label name cannot exceed 30 characters");
    }

    [Theory]
    [InlineData("EF4444")]           // Missing #
    [InlineData("#EF444")]           // Too short
    [InlineData("#EF44445")]         // Too long
    [InlineData("#GGGGGG")]          // Invalid characters
    [InlineData("")]                 // Empty
    [InlineData("#EF-444")]          // Invalid characters
    public void Constructor_ShouldThrow_WhenColorHexIsInvalid(string invalidColor)
    {
        // Act
        var act = () => new Label(_boardId, "Bug", invalidColor);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("ColorHex must be a valid hex color in format #RRGGBB");
    }

    [Theory]
    [InlineData("#000000")]
    [InlineData("#FFFFFF")]
    [InlineData("#EF4444")]
    [InlineData("#3B82F6")]
    [InlineData("#10B981")]
    public void Constructor_ShouldAccept_ValidHexColors(string validColor)
    {
        // Act
        var label = new Label(_boardId, "Test", validColor);

        // Assert
        label.ColorHex.Should().Be(validColor.ToUpperInvariant());
    }

    [Fact]
    public void Update_ShouldUpdateName()
    {
        // Arrange
        var label = new Label(_boardId, "Bug", "#EF4444");

        // Act
        label.Update(name: "Critical");

        // Assert
        label.Name.Should().Be("Critical");
    }

    [Fact]
    public void Update_ShouldUpdateColorHex()
    {
        // Arrange
        var label = new Label(_boardId, "Bug", "#EF4444");

        // Act
        label.Update(colorHex: "#DC2626");

        // Assert
        label.ColorHex.Should().Be("#DC2626");
    }
}
