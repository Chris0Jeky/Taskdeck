using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Application.Tests.TestUtilities;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class ColumnServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IBoardRepository> _boardRepoMock;
    private readonly Mock<IColumnRepository> _columnRepoMock;
    private readonly ColumnService _service;

    public ColumnServiceTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _boardRepoMock = new Mock<IBoardRepository>();
        _columnRepoMock = new Mock<IColumnRepository>();

        _unitOfWorkMock.Setup(u => u.Boards).Returns(_boardRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Columns).Returns(_columnRepoMock.Object);

        _service = new ColumnService(_unitOfWorkMock.Object);
    }

    #region CreateColumnAsync Tests

    [Fact]
    public async Task CreateColumnAsync_ShouldReturnSuccess_WithValidData()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var dto = new CreateColumnDto(board.Id, "To Do", null, null);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);
        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(board.Id, default))
            .ReturnsAsync(new List<Column>());

        // Act
        var result = await _service.CreateColumnAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("To Do");
        result.Value.BoardId.Should().Be(board.Id);
        result.Value.Position.Should().Be(0);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task CreateColumnAsync_ShouldReturnNotFound_WhenBoardDoesNotExist()
    {
        // Arrange
        var boardId = Guid.NewGuid();
        var dto = new CreateColumnDto(boardId, "Column", null, null);

        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default))
            .ReturnsAsync((Board?)null);

        // Act
        var result = await _service.CreateColumnAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.ErrorMessage.Should().Contain("Board");
    }

    [Fact]
    public async Task CreateColumnAsync_ShouldAssignPositionAtEnd_WhenPositionNotProvided()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var existingColumn1 = TestDataBuilder.CreateColumn(board.Id, "To Do", position: 0);
        var existingColumn2 = TestDataBuilder.CreateColumn(board.Id, "Done", position: 1);

        var dto = new CreateColumnDto(board.Id, "In Progress", null, null);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);
        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(board.Id, default))
            .ReturnsAsync(new List<Column> { existingColumn1, existingColumn2 });

        // Act
        var result = await _service.CreateColumnAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Position.Should().Be(2);
    }

    [Fact]
    public async Task CreateColumnAsync_ShouldUseProvidedPosition()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var dto = new CreateColumnDto(board.Id, "Column", 5, null);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);
        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(board.Id, default))
            .ReturnsAsync(new List<Column>());

        // Act
        var result = await _service.CreateColumnAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Position.Should().Be(5);
    }

    [Fact]
    public async Task CreateColumnAsync_ShouldCreateWithWipLimit()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var dto = new CreateColumnDto(board.Id, "In Progress", null, 3);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);
        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(board.Id, default))
            .ReturnsAsync(new List<Column>());

        // Act
        var result = await _service.CreateColumnAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.WipLimit.Should().Be(3);
    }

    [Fact]
    public async Task CreateColumnAsync_ShouldReturnValidationError_WhenNameIsEmpty()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var dto = new CreateColumnDto(board.Id, "", null, null);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);

        // Act
        var result = await _service.CreateColumnAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    #endregion

    #region UpdateColumnAsync Tests

    [Fact]
    public async Task UpdateColumnAsync_ShouldUpdateName()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "To Do");
        var dto = new UpdateColumnDto { Name = "Backlog" };

        _columnRepoMock.Setup(r => r.GetByIdAsync(column.Id, default))
            .ReturnsAsync(column);

        // Act
        var result = await _service.UpdateColumnAsync(column.Id, dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        column.Name.Should().Be("Backlog");
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task UpdateColumnAsync_ShouldUpdateWipLimit()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "In Progress", wipLimit: 2);
        var dto = new UpdateColumnDto { WipLimit = 5 };

        _columnRepoMock.Setup(r => r.GetByIdAsync(column.Id, default))
            .ReturnsAsync(column);

        // Act
        var result = await _service.UpdateColumnAsync(column.Id, dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        column.WipLimit.Should().Be(5);
    }

    [Fact]
    public async Task UpdateColumnAsync_ShouldRemoveWipLimit_WhenSetToNull()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "In Progress", wipLimit: 3);
        var dto = new UpdateColumnDto { WipLimit = null };

        _columnRepoMock.Setup(r => r.GetByIdAsync(column.Id, default))
            .ReturnsAsync(column);

        // Act
        var result = await _service.UpdateColumnAsync(column.Id, dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        column.WipLimit.Should().BeNull();
    }

    [Fact]
    public async Task UpdateColumnAsync_ShouldUpdatePosition()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "To Do", position: 0);
        var dto = new UpdateColumnDto { Position = 2 };

        _columnRepoMock.Setup(r => r.GetByIdAsync(column.Id, default))
            .ReturnsAsync(column);

        // Act
        var result = await _service.UpdateColumnAsync(column.Id, dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        column.Position.Should().Be(2);
    }

    [Fact]
    public async Task UpdateColumnAsync_ShouldReturnNotFound_WhenColumnDoesNotExist()
    {
        // Arrange
        var columnId = Guid.NewGuid();
        var dto = new UpdateColumnDto { Name = "Updated" };

        _columnRepoMock.Setup(r => r.GetByIdAsync(columnId, default))
            .ReturnsAsync((Column?)null);

        // Act
        var result = await _service.UpdateColumnAsync(columnId, dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    #endregion

    #region GetColumnsByBoardIdAsync Tests

    [Fact]
    public async Task GetColumnsByBoardIdAsync_ShouldReturnColumnsInOrder()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var column1 = TestDataBuilder.CreateColumn(board.Id, "Done", position: 2);
        var column2 = TestDataBuilder.CreateColumn(board.Id, "To Do", position: 0);
        var column3 = TestDataBuilder.CreateColumn(board.Id, "In Progress", position: 1);

        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(board.Id, default))
            .ReturnsAsync(new List<Column> { column1, column2, column3 });

        // Act
        var result = await _service.GetColumnsByBoardIdAsync(board.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var columnNames = result.Value.Select(c => c.Name).ToList();
        columnNames.Should().ContainInOrder("To Do", "In Progress", "Done");
    }

    [Fact]
    public async Task GetColumnsByBoardIdAsync_ShouldReturnEmptyList_WhenNoColumns()
    {
        // Arrange
        var boardId = Guid.NewGuid();

        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default))
            .ReturnsAsync(new List<Column>());

        // Act
        var result = await _service.GetColumnsByBoardIdAsync(boardId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    #endregion

    #region DeleteColumnAsync Tests

    [Fact]
    public async Task DeleteColumnAsync_ShouldDeleteColumn_WhenEmpty()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "To Do");

        _columnRepoMock.Setup(r => r.GetByIdWithCardsAsync(column.Id, default))
            .ReturnsAsync(column);

        // Act
        var result = await _service.DeleteColumnAsync(column.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _columnRepoMock.Verify(r => r.DeleteAsync(column, default), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task DeleteColumnAsync_ShouldReturnConflict_WhenColumnContainsCards()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "To Do");
        var card = TestDataBuilder.CreateCard(board.Id, column.Id, "Task");

        column.GetType().GetProperty("Cards")!.SetValue(column, new List<Card> { card });

        _columnRepoMock.Setup(r => r.GetByIdWithCardsAsync(column.Id, default))
            .ReturnsAsync(column);

        // Act
        var result = await _service.DeleteColumnAsync(column.Id);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("contains cards");
        _columnRepoMock.Verify(r => r.DeleteAsync(It.IsAny<Column>(), default), Times.Never);
    }

    [Fact]
    public async Task DeleteColumnAsync_ShouldReturnNotFound_WhenColumnDoesNotExist()
    {
        // Arrange
        var columnId = Guid.NewGuid();

        _columnRepoMock.Setup(r => r.GetByIdWithCardsAsync(columnId, default))
            .ReturnsAsync((Column?)null);

        // Act
        var result = await _service.DeleteColumnAsync(columnId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    #endregion
}
