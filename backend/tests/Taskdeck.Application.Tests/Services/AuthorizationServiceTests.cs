using FluentAssertions;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class AuthorizationServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IBoardRepository> _boardRepoMock;
    private readonly Mock<IBoardAccessRepository> _boardAccessRepoMock;
    private readonly AuthorizationService _service;

    public AuthorizationServiceTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _boardRepoMock = new Mock<IBoardRepository>();
        _boardAccessRepoMock = new Mock<IBoardAccessRepository>();

        _unitOfWorkMock.Setup(u => u.Boards).Returns(_boardRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.BoardAccesses).Returns(_boardAccessRepoMock.Object);

        _service = new AuthorizationService(_unitOfWorkMock.Object);
    }

    #region CanReadBoardAsync Tests

    [Fact]
    public async Task CanReadBoardAsync_ShouldReturnFalse_WhenBoardHasNullOwnerIdAndNoExplicitAccess()
    {
        // Arrange
        var board = new Board("Test Board");
        var userId = Guid.NewGuid();

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);
        _boardAccessRepoMock.Setup(r => r.GetByBoardAndUserAsync(board.Id, userId, default))
            .ReturnsAsync((BoardAccess?)null);

        // Act
        var result = await _service.CanReadBoardAsync(userId, board.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task CanReadBoardAsync_ShouldReturnTrue_WhenBoardHasNullOwnerIdButUserHasExplicitAccess()
    {
        var board = new Board("Test Board");
        var userId = Guid.NewGuid();
        var grantedBy = Guid.NewGuid();
        var access = new BoardAccess(board.Id, userId, UserRole.Viewer, grantedBy);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);
        _boardAccessRepoMock.Setup(r => r.GetByBoardAndUserAsync(board.Id, userId, default))
            .ReturnsAsync(access);

        var result = await _service.CanReadBoardAsync(userId, board.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task CanReadBoardAsync_ShouldReturnTrue_WhenUserIsOwner()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var board = new Board("Test Board", ownerId: ownerId);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);

        // Act
        var result = await _service.CanReadBoardAsync(ownerId, board.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task CanReadBoardAsync_ShouldReturnTrue_WhenUserHasBoardAccess()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var board = new Board("Test Board", ownerId: ownerId);
        var access = new BoardAccess(board.Id, userId, UserRole.Viewer, ownerId);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);
        _boardAccessRepoMock.Setup(r => r.GetByBoardAndUserAsync(board.Id, userId, default))
            .ReturnsAsync(access);

        // Act
        var result = await _service.CanReadBoardAsync(userId, board.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task CanReadBoardAsync_ShouldReturnFalse_WhenUserHasNoAccess()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var board = new Board("Test Board", ownerId: ownerId);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);
        _boardAccessRepoMock.Setup(r => r.GetByBoardAndUserAsync(board.Id, userId, default))
            .ReturnsAsync((BoardAccess?)null);

        // Act
        var result = await _service.CanReadBoardAsync(userId, board.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task CanReadBoardAsync_ShouldReturnNotFound_WhenBoardDoesNotExist()
    {
        // Arrange
        var boardId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default))
            .ReturnsAsync((Board?)null);

        // Act
        var result = await _service.CanReadBoardAsync(userId, boardId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task CanReadBoardAsync_ShouldReturnTrue_WhenSandboxModeIsEnabled()
    {
        var ownerId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var board = new Board("Test Board", ownerId: ownerId);
        var sandboxService = new AuthorizationService(
            _unitOfWorkMock.Object,
            new DevelopmentSandboxSettings { Enabled = true });

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);

        var result = await sandboxService.CanReadBoardAsync(userId, board.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
        _boardAccessRepoMock.Verify(r => r.GetByBoardAndUserAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), default), Times.Never);
    }

    #endregion

    #region CanWriteBoardAsync Tests

    [Fact]
    public async Task CanWriteBoardAsync_ShouldReturnTrue_WhenUserIsOwner()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var board = new Board("Test Board", ownerId: ownerId);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);

        // Act
        var result = await _service.CanWriteBoardAsync(ownerId, board.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task CanWriteBoardAsync_ShouldReturnFalse_WhenUserIsViewer()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var board = new Board("Test Board", ownerId: ownerId);
        var access = new BoardAccess(board.Id, userId, UserRole.Viewer, ownerId);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);
        _boardAccessRepoMock.Setup(r => r.GetByBoardAndUserAsync(board.Id, userId, default))
            .ReturnsAsync(access);

        // Act
        var result = await _service.CanWriteBoardAsync(userId, board.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    #endregion

    #region CanDeleteBoardAsync Tests

    [Fact]
    public async Task CanDeleteBoardAsync_ShouldReturnTrue_WhenUserIsOwner()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var board = new Board("Test Board", ownerId: ownerId);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);

        // Act
        var result = await _service.CanDeleteBoardAsync(ownerId, board.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task CanDeleteBoardAsync_ShouldReturnFalse_WhenUserIsEditor()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var board = new Board("Test Board", ownerId: ownerId);
        var access = new BoardAccess(board.Id, userId, UserRole.Editor, ownerId);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);
        _boardAccessRepoMock.Setup(r => r.GetByBoardAndUserAsync(board.Id, userId, default))
            .ReturnsAsync(access);

        // Act
        var result = await _service.CanDeleteBoardAsync(userId, board.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    #endregion

    #region GetUserRoleForBoardAsync Tests

    [Fact]
    public async Task GetUserRoleForBoardAsync_ShouldReturnOwner_WhenUserIsOwner()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var board = new Board("Test Board", ownerId: ownerId);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);

        // Act
        var result = await _service.GetUserRoleForBoardAsync(ownerId, board.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(UserRole.Owner);
    }

    [Fact]
    public async Task GetUserRoleForBoardAsync_ShouldReturnNull_WhenUserHasNoAccess()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var board = new Board("Test Board", ownerId: ownerId);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);
        _boardAccessRepoMock.Setup(r => r.GetByBoardAndUserAsync(board.Id, userId, default))
            .ReturnsAsync((BoardAccess?)null);

        // Act
        var result = await _service.GetUserRoleForBoardAsync(userId, board.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task GetUserRoleForBoardAsync_ShouldReturnNotFound_WhenBoardDoesNotExist()
    {
        // Arrange
        var boardId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default))
            .ReturnsAsync((Board?)null);

        // Act
        var result = await _service.GetUserRoleForBoardAsync(userId, boardId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    #endregion

    #region GetReadableBoardIdsAsync Tests

    [Fact]
    public async Task GetReadableBoardIdsAsync_ShouldReturnOwnedAndGrantedBoardIds()
    {
        var ownerId = Guid.NewGuid();
        var grantedBy = Guid.NewGuid();

        var ownedBoard = new Board("Owned Board", ownerId: ownerId);
        var grantedBoard = new Board("Granted Board", ownerId: Guid.NewGuid());
        var noAccessBoard = new Board("No Access Board", ownerId: Guid.NewGuid());

        var grantedAccess = new BoardAccess(grantedBoard.Id, ownerId, UserRole.Viewer, grantedBy);

        _boardAccessRepoMock.Setup(r => r.GetByUserIdAsync(ownerId, default))
            .ReturnsAsync(new List<BoardAccess> { grantedAccess });

        var result = await _service.GetReadableBoardIdsAsync(
            ownerId,
            new[] { ownedBoard, grantedBoard, noAccessBoard });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain(ownedBoard.Id);
        result.Value.Should().Contain(grantedBoard.Id);
        result.Value.Should().NotContain(noAccessBoard.Id);
        _boardAccessRepoMock.Verify(r => r.GetByUserIdAsync(ownerId, default), Times.Once);
    }

    [Fact]
    public async Task GetReadableBoardIdsAsync_ShouldReturnValidationError_WhenUserIdIsEmpty()
    {
        var board = new Board("Test Board", ownerId: Guid.NewGuid());
        var result = await _service.GetReadableBoardIdsAsync(Guid.Empty, new[] { board });

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    #endregion
}
