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

public class BoardServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IBoardRepository> _boardRepoMock;
    private readonly BoardService _service;

    public BoardServiceTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _boardRepoMock = new Mock<IBoardRepository>();

        _unitOfWorkMock.Setup(u => u.Boards).Returns(_boardRepoMock.Object);

        _service = new BoardService(_unitOfWorkMock.Object);
    }

    #region CreateBoardAsync Tests

    [Fact]
    public async Task CreateBoardAsync_ShouldReturnSuccess_WithValidData()
    {
        // Arrange
        var dto = new CreateBoardDto("Test Board", "Test description");

        _boardRepoMock.Setup(r => r.AddAsync(It.IsAny<Board>(), default))
            .ReturnsAsync((Board b, CancellationToken ct) => b);

        // Act
        var result = await _service.CreateBoardAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Test Board");
        result.Value.Description.Should().Be("Test description");
        result.Value.IsArchived.Should().BeFalse();
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task CreateBoardAsync_ShouldReturnValidationError_WhenNameIsEmpty()
    {
        // Arrange
        var dto = new CreateBoardDto("", "Description");

        // Act
        var result = await _service.CreateBoardAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task CreateBoardAsync_ShouldReturnValidationError_WhenNameIsTooLong()
    {
        // Arrange
        var longName = new string('a', 101); // Exceeds 100 char limit
        var dto = new CreateBoardDto(longName, "Description");

        // Act
        var result = await _service.CreateBoardAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task CreateBoardAsync_ShouldCreateBoard_WithNullDescription()
    {
        // Arrange
        var dto = new CreateBoardDto("Test Board", null);

        _boardRepoMock.Setup(r => r.AddAsync(It.IsAny<Board>(), default))
            .ReturnsAsync((Board b, CancellationToken ct) => b);

        // Act
        var result = await _service.CreateBoardAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Test Board");
        result.Value.Description.Should().BeNull();
    }

    #endregion

    #region UpdateBoardAsync Tests

    [Fact]
    public async Task UpdateBoardAsync_ShouldUpdateName()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard("Original Name");
        var dto = new UpdateBoardDto(Name: "Updated Name", Description: null, IsArchived: null);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);

        // Act
        var result = await _service.UpdateBoardAsync(board.Id, dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        board.Name.Should().Be("Updated Name");
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task UpdateBoardAsync_ShouldUpdateDescription()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var dto = new UpdateBoardDto(Name: null, Description: "New description", IsArchived: null);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);

        // Act
        var result = await _service.UpdateBoardAsync(board.Id, dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        board.Description.Should().Be("New description");
    }

    [Fact]
    public async Task UpdateBoardAsync_ShouldArchiveBoard_WhenIsArchivedIsTrue()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var dto = new UpdateBoardDto(Name: null, Description: null, IsArchived: true);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);

        // Act
        var result = await _service.UpdateBoardAsync(board.Id, dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        board.IsArchived.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateBoardAsync_ShouldUnarchiveBoard_WhenIsArchivedIsFalse()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard(isArchived: true);
        var dto = new UpdateBoardDto(Name: null, Description: null, IsArchived: false);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);

        // Act
        var result = await _service.UpdateBoardAsync(board.Id, dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        board.IsArchived.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateBoardAsync_ShouldReturnNotFound_WhenBoardDoesNotExist()
    {
        // Arrange
        var boardId = Guid.NewGuid();
        var dto = new UpdateBoardDto(Name: "Updated", Description: null, IsArchived: null);

        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default))
            .ReturnsAsync((Board?)null);

        // Act
        var result = await _service.UpdateBoardAsync(boardId, dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.ErrorMessage.Should().Contain("Board");
    }

    [Fact]
    public async Task UpdateBoardAsync_ShouldUpdateMultipleFields()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var dto = new UpdateBoardDto(
            Name: "New Name",
            Description: "New Description",
            IsArchived: true
        );

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);

        // Act
        var result = await _service.UpdateBoardAsync(board.Id, dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        board.Name.Should().Be("New Name");
        board.Description.Should().Be("New Description");
        board.IsArchived.Should().BeTrue();
    }

    #endregion

    #region GetBoardByIdAsync Tests

    [Fact]
    public async Task GetBoardByIdAsync_ShouldReturnBoard_WhenExists()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard("Test Board");

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);

        // Act
        var result = await _service.GetBoardByIdAsync(board.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(board.Id);
        result.Value.Name.Should().Be("Test Board");
    }

    [Fact]
    public async Task GetBoardByIdAsync_ShouldReturnNotFound_WhenBoardDoesNotExist()
    {
        // Arrange
        var boardId = Guid.NewGuid();

        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default))
            .ReturnsAsync((Board?)null);

        // Act
        var result = await _service.GetBoardByIdAsync(boardId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    #endregion

    #region GetBoardDetailAsync Tests

    [Fact]
    public async Task GetBoardDetailAsync_ShouldReturnBoardWithColumns()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard("Test Board");
        var column1 = TestDataBuilder.CreateColumn(board.Id, "To Do", position: 0);
        var column2 = TestDataBuilder.CreateColumn(board.Id, "Done", position: 1);

        // Set up columns collection
        var columns = new List<Column> { column1, column2 };
        board.GetType().GetProperty("Columns")!.SetValue(board, columns);

        _boardRepoMock.Setup(r => r.GetByIdWithDetailsAsync(board.Id, default))
            .ReturnsAsync(board);

        // Act
        var result = await _service.GetBoardDetailAsync(board.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Columns.Should().HaveCount(2);
        result.Value.Columns.First().Name.Should().Be("To Do");
    }

    [Fact]
    public async Task GetBoardDetailAsync_ShouldReturnColumnsInOrder()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var column1 = TestDataBuilder.CreateColumn(board.Id, "Done", position: 2);
        var column2 = TestDataBuilder.CreateColumn(board.Id, "To Do", position: 0);
        var column3 = TestDataBuilder.CreateColumn(board.Id, "In Progress", position: 1);

        var columns = new List<Column> { column1, column2, column3 };
        board.GetType().GetProperty("Columns")!.SetValue(board, columns);

        _boardRepoMock.Setup(r => r.GetByIdWithDetailsAsync(board.Id, default))
            .ReturnsAsync(board);

        // Act
        var result = await _service.GetBoardDetailAsync(board.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var columnNames = result.Value.Columns.Select(c => c.Name).ToList();
        columnNames.Should().ContainInOrder("To Do", "In Progress", "Done");
    }

    [Fact]
    public async Task GetBoardDetailAsync_ShouldReturnNotFound_WhenBoardDoesNotExist()
    {
        // Arrange
        var boardId = Guid.NewGuid();

        _boardRepoMock.Setup(r => r.GetByIdWithDetailsAsync(boardId, default))
            .ReturnsAsync((Board?)null);

        // Act
        var result = await _service.GetBoardDetailAsync(boardId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    #endregion

    #region ListBoardsAsync Tests

    [Fact]
    public async Task ListBoardsAsync_ShouldReturnAllBoards_WhenNoFilters()
    {
        // Arrange
        var boards = new List<Board>
        {
            TestDataBuilder.CreateBoard("Board 1"),
            TestDataBuilder.CreateBoard("Board 2")
        };

        _boardRepoMock.Setup(r => r.SearchAsync(null, false, default))
            .ReturnsAsync(boards);

        // Act
        var result = await _service.ListBoardsAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListBoardsAsync_ShouldFilterBySearchText()
    {
        // Arrange
        var matchingBoard = TestDataBuilder.CreateBoard("My Project");

        _boardRepoMock.Setup(r => r.SearchAsync("Project", false, default))
            .ReturnsAsync(new List<Board> { matchingBoard });

        // Act
        var result = await _service.ListBoardsAsync(searchText: "Project");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value.First().Name.Should().Contain("Project");
    }

    [Fact]
    public async Task ListBoardsAsync_ShouldIncludeArchived_WhenRequested()
    {
        // Arrange
        var activeBoard = TestDataBuilder.CreateBoard("Active");
        var archivedBoard = TestDataBuilder.CreateBoard("Archived", isArchived: true);

        _boardRepoMock.Setup(r => r.SearchAsync(null, true, default))
            .ReturnsAsync(new List<Board> { activeBoard, archivedBoard });

        // Act
        var result = await _service.ListBoardsAsync(includeArchived: true);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Should().Contain(b => b.IsArchived);
    }

    [Fact]
    public async Task ListBoardsAsync_ShouldExcludeArchived_ByDefault()
    {
        // Arrange
        var activeBoard = TestDataBuilder.CreateBoard("Active");

        _boardRepoMock.Setup(r => r.SearchAsync(null, false, default))
            .ReturnsAsync(new List<Board> { activeBoard });

        // Act
        var result = await _service.ListBoardsAsync(includeArchived: false);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().AllSatisfy(b => b.IsArchived.Should().BeFalse());
    }

    #endregion

    #region DeleteBoardAsync Tests

    [Fact]
    public async Task DeleteBoardAsync_ShouldArchiveBoard_WhenExists()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);

        // Act
        var result = await _service.DeleteBoardAsync(board.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
        board.IsArchived.Should().BeTrue();
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task DeleteBoardAsync_ShouldReturnNotFound_WhenBoardDoesNotExist()
    {
        // Arrange
        var boardId = Guid.NewGuid();

        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default))
            .ReturnsAsync((Board?)null);

        // Act
        var result = await _service.DeleteBoardAsync(boardId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task DeleteBoardAsync_ShouldPerformSoftDelete()
    {
        // Arrange - Board should be archived, not permanently deleted
        var board = TestDataBuilder.CreateBoard();

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);

        // Act
        var result = await _service.DeleteBoardAsync(board.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
        // Verify that the board repository's delete method was NOT called (soft delete)
        _boardRepoMock.Verify(r => r.DeleteAsync(It.IsAny<Board>(), default), Times.Never);
        // But Archive was called
        board.IsArchived.Should().BeTrue();
    }

    #endregion
}
