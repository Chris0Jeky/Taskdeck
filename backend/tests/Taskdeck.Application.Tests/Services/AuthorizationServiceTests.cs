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

        _boardRepoMock.Setup(r => r.GetOwnedBoardIdsAsync(
                ownerId,
                It.IsAny<IEnumerable<Guid>>(),
                default))
            .ReturnsAsync(new List<Guid> { ownedBoard.Id });
        _boardAccessRepoMock.Setup(r => r.GetByUserIdAsync(ownerId, default))
            .ReturnsAsync(new List<BoardAccess> { grantedAccess });

        var result = await _service.GetReadableBoardIdsAsync(
            ownerId,
            new[] { ownedBoard.Id, grantedBoard.Id, noAccessBoard.Id });

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
        var result = await _service.GetReadableBoardIdsAsync(Guid.Empty, new[] { board.Id });

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    #endregion

    #region GetWritableBoardIdsAsync Tests

    [Fact]
    public async Task GetWritableBoardIdsAsync_ShouldAdmitOwnerAndWriteCapableRoles_AndRejectViewerAndNonMember()
    {
        // The admitted set must be exactly what BoardAccess.CanWrite() admits, plus ownership.
        var userId = Guid.NewGuid();
        var grantedBy = Guid.NewGuid();
        var otherOwner = Guid.NewGuid();

        var ownedBoard = new Board("Owned", ownerId: userId);
        var adminBoard = new Board("Admin member", ownerId: otherOwner);
        var editorBoard = new Board("Editor member", ownerId: otherOwner);
        var ownerRoleBoard = new Board("Owner-role member", ownerId: otherOwner);
        var viewerBoard = new Board("Viewer member", ownerId: otherOwner);
        var strangerBoard = new Board("No membership at all", ownerId: otherOwner);

        _boardRepoMock.Setup(r => r.GetOwnedBoardIdsAsync(userId, It.IsAny<IEnumerable<Guid>>(), default))
            .ReturnsAsync(new List<Guid> { ownedBoard.Id });
        _boardAccessRepoMock.Setup(r => r.GetByUserIdAsync(userId, default))
            .ReturnsAsync(new List<BoardAccess>
            {
                new(adminBoard.Id, userId, UserRole.Admin, grantedBy),
                new(editorBoard.Id, userId, UserRole.Editor, grantedBy),
                new(ownerRoleBoard.Id, userId, UserRole.Owner, grantedBy),
                new(viewerBoard.Id, userId, UserRole.Viewer, grantedBy),
            });

        var result = await _service.GetWritableBoardIdsAsync(
            userId,
            new[] { ownedBoard.Id, adminBoard.Id, editorBoard.Id, ownerRoleBoard.Id, viewerBoard.Id, strangerBoard.Id });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(new[]
        {
            ownedBoard.Id,
            adminBoard.Id,
            editorBoard.Id,
            ownerRoleBoard.Id,
        });
        result.Value.Should().NotContain(viewerBoard.Id);
        result.Value.Should().NotContain(strangerBoard.Id);
    }

    [Fact]
    public async Task GetWritableBoardIdsAsync_ShouldUseOneBatchedLookupPerRepository_NotOnePerBoard()
    {
        // The whole point of the batched form: six candidate boards must still cost one
        // ownership query and one membership read.
        var userId = Guid.NewGuid();
        var grantedBy = Guid.NewGuid();
        var boardIds = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();

        _boardRepoMock.Setup(r => r.GetOwnedBoardIdsAsync(userId, It.IsAny<IEnumerable<Guid>>(), default))
            .ReturnsAsync(new List<Guid> { boardIds[0] });
        _boardAccessRepoMock.Setup(r => r.GetByUserIdAsync(userId, default))
            .ReturnsAsync(new List<BoardAccess> { new(boardIds[1], userId, UserRole.Editor, grantedBy) });

        var result = await _service.GetWritableBoardIdsAsync(userId, boardIds);

        result.IsSuccess.Should().BeTrue();
        _boardRepoMock.Verify(
            r => r.GetOwnedBoardIdsAsync(userId, It.IsAny<IEnumerable<Guid>>(), default),
            Times.Once);
        _boardAccessRepoMock.Verify(r => r.GetByUserIdAsync(userId, default), Times.Once);
        // Never the per-board path: a single-board fetch is the N+1 this method exists to avoid.
        _boardRepoMock.Verify(
            r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetWritableBoardIdsAsync_ShouldSkipMembershipRead_WhenEveryCandidateIsOwned()
    {
        var userId = Guid.NewGuid();
        var boardIds = new[] { Guid.NewGuid(), Guid.NewGuid() };

        _boardRepoMock.Setup(r => r.GetOwnedBoardIdsAsync(userId, It.IsAny<IEnumerable<Guid>>(), default))
            .ReturnsAsync(boardIds.ToList());

        var result = await _service.GetWritableBoardIdsAsync(userId, boardIds);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(boardIds);
        _boardAccessRepoMock.Verify(r => r.GetByUserIdAsync(userId, default), Times.Never);
    }

    [Fact]
    public async Task GetWritableBoardIdsAsync_ShouldReturnEmpty_AndTouchNoRepository_ForEmptyCandidateSet()
    {
        var userId = Guid.NewGuid();

        var result = await _service.GetWritableBoardIdsAsync(userId, Array.Empty<Guid>());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
        _boardRepoMock.Verify(
            r => r.GetOwnedBoardIdsAsync(It.IsAny<Guid>(), It.IsAny<IEnumerable<Guid>>(), default),
            Times.Never);
        _boardAccessRepoMock.Verify(r => r.GetByUserIdAsync(It.IsAny<Guid>(), default), Times.Never);
    }

    [Fact]
    public async Task GetWritableBoardIdsAsync_ShouldReturnValidationError_WhenUserIdIsEmpty()
    {
        var board = new Board("Test Board", ownerId: Guid.NewGuid());

        var result = await _service.GetWritableBoardIdsAsync(Guid.Empty, new[] { board.Id });

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    #endregion
}
