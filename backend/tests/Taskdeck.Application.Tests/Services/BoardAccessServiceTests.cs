using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class BoardAccessServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IBoardAccessRepository> _boardAccessRepoMock;
    private readonly Mock<IBoardRepository> _boardRepoMock;
    private readonly Mock<IUserRepository> _userRepoMock;
    private readonly BoardAccessService _service;

    public BoardAccessServiceTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _boardAccessRepoMock = new Mock<IBoardAccessRepository>();
        _boardRepoMock = new Mock<IBoardRepository>();
        _userRepoMock = new Mock<IUserRepository>();

        _unitOfWorkMock.Setup(u => u.BoardAccesses).Returns(_boardAccessRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Boards).Returns(_boardRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Users).Returns(_userRepoMock.Object);

        _service = new BoardAccessService(_unitOfWorkMock.Object);
    }

    [Fact]
    public async Task GrantAccessAsync_ShouldReturnSuccess_WithValidData()
    {
        var granter = CreateUser("granter");
        var targetUser = CreateUser("target");
        var board = new Board("Test Board", ownerId: granter.Id);
        var dto = new GrantAccessDto(board.Id, targetUser.Id, UserRole.Editor);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default)).ReturnsAsync(board);
        _userRepoMock.Setup(r => r.GetByIdAsync(granter.Id, default)).ReturnsAsync(granter);
        _userRepoMock.Setup(r => r.GetByIdAsync(targetUser.Id, default)).ReturnsAsync(targetUser);
        _boardAccessRepoMock.Setup(r => r.GetByBoardAndUserAsync(board.Id, targetUser.Id, default))
            .ReturnsAsync((BoardAccess?)null);
        _boardAccessRepoMock.Setup(r => r.AddAsync(It.IsAny<BoardAccess>(), default))
            .ReturnsAsync((BoardAccess a, CancellationToken ct) => a);

        var result = await _service.GrantAccessAsync(dto, granter.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.BoardId.Should().Be(board.Id);
        result.Value.UserId.Should().Be(targetUser.Id);
        result.Value.Role.Should().Be(UserRole.Editor);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task GrantAccessAsync_ShouldReturnForbidden_WhenGranterCannotManageAccess()
    {
        var owner = CreateUser("owner");
        var granter = CreateUser("viewer");
        var targetUser = CreateUser("target");
        var board = new Board("Test Board", ownerId: owner.Id);
        var granterAccess = new BoardAccess(board.Id, granter.Id, UserRole.Viewer, owner.Id);
        var dto = new GrantAccessDto(board.Id, targetUser.Id, UserRole.Editor);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default)).ReturnsAsync(board);
        _userRepoMock.Setup(r => r.GetByIdAsync(granter.Id, default)).ReturnsAsync(granter);
        _userRepoMock.Setup(r => r.GetByIdAsync(targetUser.Id, default)).ReturnsAsync(targetUser);
        _boardAccessRepoMock.Setup(r => r.GetByBoardAndUserAsync(board.Id, granter.Id, default))
            .ReturnsAsync(granterAccess);

        var result = await _service.GrantAccessAsync(dto, granter.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task GrantAccessAsync_ShouldBootstrapOwner_WhenBoardHasNoOwnerAndNoExistingAccess()
    {
        var granter = CreateUser("granter");
        var targetUser = CreateUser("target");
        var board = new Board("Legacy Board");
        var dto = new GrantAccessDto(board.Id, targetUser.Id, UserRole.Editor);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default)).ReturnsAsync(board);
        _userRepoMock.Setup(r => r.GetByIdAsync(granter.Id, default)).ReturnsAsync(granter);
        _userRepoMock.Setup(r => r.GetByIdAsync(targetUser.Id, default)).ReturnsAsync(targetUser);
        _boardAccessRepoMock.Setup(r => r.GetByBoardIdAsync(board.Id, default))
            .ReturnsAsync(Array.Empty<BoardAccess>());
        _boardAccessRepoMock.Setup(r => r.GetByBoardAndUserAsync(board.Id, granter.Id, default))
            .ReturnsAsync((BoardAccess?)null);
        _boardAccessRepoMock.Setup(r => r.GetByBoardAndUserAsync(board.Id, targetUser.Id, default))
            .ReturnsAsync((BoardAccess?)null);
        _boardAccessRepoMock.Setup(r => r.AddAsync(It.IsAny<BoardAccess>(), default))
            .ReturnsAsync((BoardAccess a, CancellationToken _) => a);

        var result = await _service.GrantAccessAsync(dto, granter.Id);

        result.IsSuccess.Should().BeTrue();
        board.OwnerId.Should().Be(granter.Id);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task GrantAccessAsync_ShouldReturnConflict_WhenAccessAlreadyExists()
    {
        var granter = CreateUser("granter");
        var targetUser = CreateUser("target");
        var board = new Board("Test Board", ownerId: granter.Id);
        var existingAccess = new BoardAccess(board.Id, targetUser.Id, UserRole.Viewer, granter.Id);
        var dto = new GrantAccessDto(board.Id, targetUser.Id, UserRole.Editor);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default)).ReturnsAsync(board);
        _userRepoMock.Setup(r => r.GetByIdAsync(granter.Id, default)).ReturnsAsync(granter);
        _userRepoMock.Setup(r => r.GetByIdAsync(targetUser.Id, default)).ReturnsAsync(targetUser);
        _boardAccessRepoMock.Setup(r => r.GetByBoardAndUserAsync(board.Id, targetUser.Id, default))
            .ReturnsAsync(existingAccess);

        var result = await _service.GrantAccessAsync(dto, granter.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task GrantAccessAsync_ShouldReturnForbidden_WhenOwnerlessBoardHasExistingAccessAndGranterCannotManage()
    {
        var granter = CreateUser("granter");
        var targetUser = CreateUser("target");
        var existingUser = CreateUser("existing");
        var board = new Board("Legacy Board");
        var existingAccess = new BoardAccess(board.Id, existingUser.Id, UserRole.Viewer, granter.Id);
        var dto = new GrantAccessDto(board.Id, targetUser.Id, UserRole.Editor);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default)).ReturnsAsync(board);
        _userRepoMock.Setup(r => r.GetByIdAsync(granter.Id, default)).ReturnsAsync(granter);
        _userRepoMock.Setup(r => r.GetByIdAsync(targetUser.Id, default)).ReturnsAsync(targetUser);
        _boardAccessRepoMock.Setup(r => r.GetByBoardIdAsync(board.Id, default))
            .ReturnsAsync(new[] { existingAccess });
        _boardAccessRepoMock.Setup(r => r.GetByBoardAndUserAsync(board.Id, granter.Id, default))
            .ReturnsAsync((BoardAccess?)null);

        var result = await _service.GrantAccessAsync(dto, granter.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task GrantAccessAsync_ShouldBypassManageChecks_WhenSandboxModeIsEnabled()
    {
        var owner = CreateUser("owner");
        var granter = CreateUser("granter");
        var targetUser = CreateUser("target");
        var board = new Board("Test Board", ownerId: owner.Id);
        var dto = new GrantAccessDto(board.Id, targetUser.Id, UserRole.Editor);
        var sandboxService = new BoardAccessService(
            _unitOfWorkMock.Object,
            new DevelopmentSandboxSettings { Enabled = true });

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default)).ReturnsAsync(board);
        _userRepoMock.Setup(r => r.GetByIdAsync(granter.Id, default)).ReturnsAsync(granter);
        _userRepoMock.Setup(r => r.GetByIdAsync(targetUser.Id, default)).ReturnsAsync(targetUser);
        _boardAccessRepoMock.Setup(r => r.GetByBoardAndUserAsync(board.Id, targetUser.Id, default))
            .ReturnsAsync((BoardAccess?)null);
        _boardAccessRepoMock.Setup(r => r.AddAsync(It.IsAny<BoardAccess>(), default))
            .ReturnsAsync((BoardAccess a, CancellationToken _) => a);

        var result = await sandboxService.GrantAccessAsync(dto, granter.Id);

        result.IsSuccess.Should().BeTrue();
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task UpdateAccessAsync_ShouldReturnSuccess_WhenUpdatingRole()
    {
        var owner = CreateUser("owner");
        var targetUser = CreateUser("target");
        var board = new Board("Test Board", ownerId: owner.Id);
        var access = new BoardAccess(board.Id, targetUser.Id, UserRole.Viewer, owner.Id);
        var dto = new UpdateAccessDto(UserRole.Editor);

        _boardAccessRepoMock.Setup(r => r.GetByIdAsync(access.Id, default)).ReturnsAsync(access);
        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default)).ReturnsAsync(board);
        _userRepoMock.Setup(r => r.GetByIdAsync(owner.Id, default)).ReturnsAsync(owner);

        var result = await _service.UpdateAccessAsync(board.Id, access.Id, dto, owner.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Role.Should().Be(UserRole.Editor);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task UpdateAccessAsync_ShouldReturnNotFound_WhenAccessDoesNotBelongToBoard()
    {
        var owner = CreateUser("owner");
        var board = new Board("Board 1", ownerId: owner.Id);
        var otherBoard = new Board("Board 2", ownerId: owner.Id);
        var access = new BoardAccess(otherBoard.Id, Guid.NewGuid(), UserRole.Viewer, owner.Id);

        _boardAccessRepoMock.Setup(r => r.GetByIdAsync(access.Id, default)).ReturnsAsync(access);

        var result = await _service.UpdateAccessAsync(board.Id, access.Id, new UpdateAccessDto(UserRole.Editor), owner.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task RevokeAccessAsync_ShouldReturnSuccess_WhenAccessExistsAndUserCanManage()
    {
        var owner = CreateUser("owner");
        var targetUser = CreateUser("target");
        var board = new Board("Test Board", ownerId: owner.Id);
        var access = new BoardAccess(board.Id, targetUser.Id, UserRole.Editor, owner.Id);

        _boardAccessRepoMock.Setup(r => r.GetByIdAsync(access.Id, default)).ReturnsAsync(access);
        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default)).ReturnsAsync(board);
        _userRepoMock.Setup(r => r.GetByIdAsync(owner.Id, default)).ReturnsAsync(owner);

        var result = await _service.RevokeAccessAsync(board.Id, access.Id, owner.Id);

        result.IsSuccess.Should().BeTrue();
        _boardAccessRepoMock.Verify(r => r.DeleteAsync(access, default), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task RevokeAccessAsync_ShouldReturnForbidden_WhenUserCannotManage()
    {
        var owner = CreateUser("owner");
        var viewer = CreateUser("viewer");
        var targetUser = CreateUser("target");
        var board = new Board("Test Board", ownerId: owner.Id);
        var access = new BoardAccess(board.Id, targetUser.Id, UserRole.Editor, owner.Id);
        var viewerAccess = new BoardAccess(board.Id, viewer.Id, UserRole.Viewer, owner.Id);

        _boardAccessRepoMock.Setup(r => r.GetByIdAsync(access.Id, default)).ReturnsAsync(access);
        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default)).ReturnsAsync(board);
        _userRepoMock.Setup(r => r.GetByIdAsync(viewer.Id, default)).ReturnsAsync(viewer);
        _boardAccessRepoMock.Setup(r => r.GetByBoardAndUserAsync(board.Id, viewer.Id, default))
            .ReturnsAsync(viewerAccess);

        var result = await _service.RevokeAccessAsync(board.Id, access.Id, viewer.Id);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        _boardAccessRepoMock.Verify(r => r.DeleteAsync(It.IsAny<BoardAccess>(), default), Times.Never);
    }

    [Fact]
    public async Task GetBoardAccessListAsync_ShouldReturnNotFound_WhenBoardDoesNotExist()
    {
        var boardId = Guid.NewGuid();
        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync((Board?)null);

        var result = await _service.GetBoardAccessListAsync(boardId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task GetUserBoardsAsync_ShouldReturnNotFound_WhenUserDoesNotExist()
    {
        var userId = Guid.NewGuid();
        _userRepoMock.Setup(r => r.GetByIdAsync(userId, default)).ReturnsAsync((User?)null);

        var result = await _service.GetUserBoardsAsync(userId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    private static User CreateUser(string stem)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return new User($"{stem}_{suffix}", $"{stem}_{suffix}@example.com", "hashedpassword");
    }
}
