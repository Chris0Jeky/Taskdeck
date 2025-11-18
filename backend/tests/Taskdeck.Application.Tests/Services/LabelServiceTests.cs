using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Application.Tests.TestUtilities;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class LabelServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IBoardRepository> _boardRepoMock;
    private readonly Mock<ILabelRepository> _labelRepoMock;
    private readonly LabelService _service;

    public LabelServiceTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _boardRepoMock = new Mock<IBoardRepository>();
        _labelRepoMock = new Mock<ILabelRepository>();

        _unitOfWorkMock.Setup(u => u.Boards).Returns(_boardRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Labels).Returns(_labelRepoMock.Object);

        _service = new LabelService(_unitOfWorkMock.Object);
    }

    #region CreateLabelAsync Tests

    [Fact]
    public async Task CreateLabelAsync_ShouldReturnSuccess_WithValidData()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var dto = new CreateLabelDto(board.Id, "Bug", "#FF0000");

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);

        // Act
        var result = await _service.CreateLabelAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Bug");
        result.Value.ColorHex.Should().Be("#FF0000");
        result.Value.BoardId.Should().Be(board.Id);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task CreateLabelAsync_ShouldReturnNotFound_WhenBoardDoesNotExist()
    {
        // Arrange
        var boardId = Guid.NewGuid();
        var dto = new CreateLabelDto(boardId, "Label", "#FF0000");

        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default))
            .ReturnsAsync((Board?)null);

        // Act
        var result = await _service.CreateLabelAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.ErrorMessage.Should().Contain("Board");
    }

    [Fact]
    public async Task CreateLabelAsync_ShouldReturnValidationError_WhenNameIsEmpty()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var dto = new CreateLabelDto(board.Id, "", "#FF0000");

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);

        // Act
        var result = await _service.CreateLabelAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task CreateLabelAsync_ShouldReturnValidationError_WhenColorHexIsInvalid()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var dto = new CreateLabelDto(board.Id, "Bug", "invalid-color");

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);

        // Act
        var result = await _service.CreateLabelAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Theory]
    [InlineData("#FF0000")]
    [InlineData("#00FF00")]
    [InlineData("#0000FF")]
    [InlineData("#123456")]
    [InlineData("#ABCDEF")]
    public async Task CreateLabelAsync_ShouldAcceptValidColorHex(string colorHex)
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var dto = new CreateLabelDto(board.Id, "Label", colorHex);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);

        // Act
        var result = await _service.CreateLabelAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ColorHex.Should().Be(colorHex);
    }

    #endregion

    #region UpdateLabelAsync Tests

    [Fact]
    public async Task UpdateLabelAsync_ShouldUpdateName()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var label = TestDataBuilder.CreateLabel(board.Id, "Bug", "#FF0000");
        var dto = new UpdateLabelDto(Name: "Critical Bug", ColorHex: null);

        _labelRepoMock.Setup(r => r.GetByIdAsync(label.Id, default))
            .ReturnsAsync(label);

        // Act
        var result = await _service.UpdateLabelAsync(label.Id, dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        label.Name.Should().Be("Critical Bug");
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task UpdateLabelAsync_ShouldUpdateColorHex()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var label = TestDataBuilder.CreateLabel(board.Id, "Bug", "#FF0000");
        var dto = new UpdateLabelDto(Name: null, ColorHex: "#00FF00");

        _labelRepoMock.Setup(r => r.GetByIdAsync(label.Id, default))
            .ReturnsAsync(label);

        // Act
        var result = await _service.UpdateLabelAsync(label.Id, dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        label.ColorHex.Should().Be("#00FF00");
    }

    [Fact]
    public async Task UpdateLabelAsync_ShouldUpdateBothFields()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var label = TestDataBuilder.CreateLabel(board.Id, "Bug", "#FF0000");
        var dto = new UpdateLabelDto(
            Name: "Feature",
            ColorHex: "#0000FF"
        );

        _labelRepoMock.Setup(r => r.GetByIdAsync(label.Id, default))
            .ReturnsAsync(label);

        // Act
        var result = await _service.UpdateLabelAsync(label.Id, dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        label.Name.Should().Be("Feature");
        label.ColorHex.Should().Be("#0000FF");
    }

    [Fact]
    public async Task UpdateLabelAsync_ShouldReturnNotFound_WhenLabelDoesNotExist()
    {
        // Arrange
        var labelId = Guid.NewGuid();
        var dto = new UpdateLabelDto(Name: "Updated", ColorHex: null);

        _labelRepoMock.Setup(r => r.GetByIdAsync(labelId, default))
            .ReturnsAsync((Label?)null);

        // Act
        var result = await _service.UpdateLabelAsync(labelId, dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task UpdateLabelAsync_ShouldReturnValidationError_WhenColorHexIsInvalid()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var label = TestDataBuilder.CreateLabel(board.Id, "Bug", "#FF0000");
        var dto = new UpdateLabelDto(Name: null, ColorHex: "not-a-color");

        _labelRepoMock.Setup(r => r.GetByIdAsync(label.Id, default))
            .ReturnsAsync(label);

        // Act
        var result = await _service.UpdateLabelAsync(label.Id, dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    #endregion

    #region GetLabelsByBoardIdAsync Tests

    [Fact]
    public async Task GetLabelsByBoardIdAsync_ShouldReturnAllLabels()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var labels = new List<Label>
        {
            TestDataBuilder.CreateLabel(board.Id, "Bug", "#FF0000"),
            TestDataBuilder.CreateLabel(board.Id, "Feature", "#00FF00"),
            TestDataBuilder.CreateLabel(board.Id, "Enhancement", "#0000FF")
        };

        _labelRepoMock.Setup(r => r.GetByBoardIdAsync(board.Id, default))
            .ReturnsAsync(labels);

        // Act
        var result = await _service.GetLabelsByBoardIdAsync(board.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);
        result.Value.Should().Contain(l => l.Name == "Bug");
        result.Value.Should().Contain(l => l.Name == "Feature");
        result.Value.Should().Contain(l => l.Name == "Enhancement");
    }

    [Fact]
    public async Task GetLabelsByBoardIdAsync_ShouldReturnEmptyList_WhenNoLabels()
    {
        // Arrange
        var boardId = Guid.NewGuid();

        _labelRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default))
            .ReturnsAsync(new List<Label>());

        // Act
        var result = await _service.GetLabelsByBoardIdAsync(boardId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    #endregion

    #region DeleteLabelAsync Tests

    [Fact]
    public async Task DeleteLabelAsync_ShouldDeleteLabel_WhenExists()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var label = TestDataBuilder.CreateLabel(board.Id, "Bug", "#FF0000");

        _labelRepoMock.Setup(r => r.GetByIdAsync(label.Id, default))
            .ReturnsAsync(label);

        // Act
        var result = await _service.DeleteLabelAsync(label.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _labelRepoMock.Verify(r => r.DeleteAsync(label, default), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task DeleteLabelAsync_ShouldReturnNotFound_WhenLabelDoesNotExist()
    {
        // Arrange
        var labelId = Guid.NewGuid();

        _labelRepoMock.Setup(r => r.GetByIdAsync(labelId, default))
            .ReturnsAsync((Label?)null);

        // Act
        var result = await _service.DeleteLabelAsync(labelId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        _labelRepoMock.Verify(r => r.DeleteAsync(It.IsAny<Label>(), default), Times.Never);
    }

    #endregion
}
