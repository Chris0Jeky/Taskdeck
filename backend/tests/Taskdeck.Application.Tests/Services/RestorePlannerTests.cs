using System.Text.Json;
using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class RestorePlannerTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IArchiveItemRepository> _archiveItemRepoMock;
    private readonly Mock<IBoardRepository> _boardRepoMock;
    private readonly Mock<IAuthorizationService> _authServiceMock;
    private readonly RestorePlanner _planner;

    public RestorePlannerTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _archiveItemRepoMock = new Mock<IArchiveItemRepository>();
        _boardRepoMock = new Mock<IBoardRepository>();

        _unitOfWorkMock.Setup(u => u.ArchiveItems).Returns(_archiveItemRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Boards).Returns(_boardRepoMock.Object);

        _authServiceMock = new Mock<IAuthorizationService>();
        _planner = new RestorePlanner(_unitOfWorkMock.Object, _authServiceMock.Object);
    }

    [Fact]
    public async Task PlanRestoreAsync_ArchiveItemNotFound_ReturnsNotFound()
    {
        _archiveItemRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync((ArchiveItem?)null);

        var dto = new RestoreArchiveItemDto(null, RestoreMode.Copy, ConflictStrategy.Fail);
        var result = await _planner.PlanRestoreAsync(Guid.NewGuid(), dto, Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task PlanRestoreAsync_ItemAlreadyRestored_ReturnsInvalidOperation()
    {
        var boardId = Guid.NewGuid();
        var snapshot = JsonSerializer.Serialize(new { Name = "Test", Description = "" });
        var archiveItem = new ArchiveItem("board", Guid.NewGuid(), boardId, "Test", Guid.NewGuid(), snapshot, null);
        // Mark as restored to make it unavailable
        archiveItem.MarkAsRestored(Guid.NewGuid());

        _archiveItemRepoMock.Setup(r => r.GetByIdAsync(archiveItem.Id, default))
            .ReturnsAsync(archiveItem);

        var dto = new RestoreArchiveItemDto(null, RestoreMode.Copy, ConflictStrategy.Fail);
        var result = await _planner.PlanRestoreAsync(archiveItem.Id, dto, Guid.NewGuid());

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
    }

    [Fact]
    public async Task PlanRestoreAsync_UserLacksPermission_ReturnsForbidden()
    {
        var boardId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var snapshot = JsonSerializer.Serialize(new { Name = "Test", Description = "" });
        var archiveItem = new ArchiveItem("board", Guid.NewGuid(), boardId, "Test", userId, snapshot, null);

        _archiveItemRepoMock.Setup(r => r.GetByIdAsync(archiveItem.Id, default))
            .ReturnsAsync(archiveItem);
        _authServiceMock.Setup(a => a.CanWriteBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Success(false));

        var dto = new RestoreArchiveItemDto(null, RestoreMode.Copy, ConflictStrategy.Fail);
        var result = await _planner.PlanRestoreAsync(archiveItem.Id, dto, userId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task PlanRestoreAsync_BoardEntity_SkipsTargetBoardValidation()
    {
        var boardId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var snapshot = JsonSerializer.Serialize(new { Name = "Test", Description = "" });
        var archiveItem = new ArchiveItem("board", Guid.NewGuid(), boardId, "Test", userId, snapshot, null);

        _archiveItemRepoMock.Setup(r => r.GetByIdAsync(archiveItem.Id, default))
            .ReturnsAsync(archiveItem);
        _authServiceMock.Setup(a => a.CanWriteBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Success(true));

        var dto = new RestoreArchiveItemDto(null, RestoreMode.Copy, ConflictStrategy.Fail);
        var result = await _planner.PlanRestoreAsync(archiveItem.Id, dto, userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.ArchiveItem.Should().Be(archiveItem);
        result.Value.TargetBoardId.Should().Be(boardId);
        // Board entity should NOT trigger GetByIdAsync for target board validation
        _boardRepoMock.Verify(b => b.GetByIdAsync(It.IsAny<Guid>(), default), Times.Never);
    }

    [Fact]
    public async Task PlanRestoreAsync_ColumnEntity_ValidatesTargetBoard()
    {
        var boardId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var snapshot = JsonSerializer.Serialize(new { Name = "Col", Position = 0 });
        var archiveItem = new ArchiveItem("column", Guid.NewGuid(), boardId, "Col", userId, snapshot, null);

        _archiveItemRepoMock.Setup(r => r.GetByIdAsync(archiveItem.Id, default))
            .ReturnsAsync(archiveItem);
        _authServiceMock.Setup(a => a.CanWriteBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Success(true));
        _boardRepoMock.Setup(b => b.GetByIdAsync(boardId, default))
            .ReturnsAsync((Board?)null);

        var dto = new RestoreArchiveItemDto(null, RestoreMode.Copy, ConflictStrategy.Fail);
        var result = await _planner.PlanRestoreAsync(archiveItem.Id, dto, userId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task PlanRestoreAsync_ColumnToArchivedBoard_ReturnsInvalidOperation()
    {
        var boardId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var snapshot = JsonSerializer.Serialize(new { Name = "Col", Position = 0 });
        var archiveItem = new ArchiveItem("column", Guid.NewGuid(), boardId, "Col", userId, snapshot, null);

        var archivedBoard = new Board("Archived", null, userId);
        archivedBoard.Archive();

        _archiveItemRepoMock.Setup(r => r.GetByIdAsync(archiveItem.Id, default))
            .ReturnsAsync(archiveItem);
        _authServiceMock.Setup(a => a.CanWriteBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Success(true));
        _boardRepoMock.Setup(b => b.GetByIdAsync(boardId, default))
            .ReturnsAsync(archivedBoard);

        var dto = new RestoreArchiveItemDto(null, RestoreMode.Copy, ConflictStrategy.Fail);
        var result = await _planner.PlanRestoreAsync(archiveItem.Id, dto, userId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
    }

    [Fact]
    public async Task PlanRestoreAsync_CustomTargetBoard_UsesOverride()
    {
        var originalBoardId = Guid.NewGuid();
        var targetBoardId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var snapshot = JsonSerializer.Serialize(new { Name = "Col", Position = 0 });
        var archiveItem = new ArchiveItem("column", Guid.NewGuid(), originalBoardId, "Col", userId, snapshot, null);

        var targetBoard = new Board("Target", null, userId);

        _archiveItemRepoMock.Setup(r => r.GetByIdAsync(archiveItem.Id, default))
            .ReturnsAsync(archiveItem);
        _authServiceMock.Setup(a => a.CanWriteBoardAsync(userId, targetBoardId))
            .ReturnsAsync(Result.Success(true));
        _boardRepoMock.Setup(b => b.GetByIdAsync(targetBoardId, default))
            .ReturnsAsync(targetBoard);

        var dto = new RestoreArchiveItemDto(targetBoardId, RestoreMode.Copy, ConflictStrategy.Fail);
        var result = await _planner.PlanRestoreAsync(archiveItem.Id, dto, userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.TargetBoardId.Should().Be(targetBoardId);
    }

    [Fact]
    public async Task PlanRestoreAsync_NoAuthService_SkipsPermissionCheck()
    {
        var plannerNoAuth = new RestorePlanner(_unitOfWorkMock.Object);

        var boardId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var snapshot = JsonSerializer.Serialize(new { Name = "Test", Description = "" });
        var archiveItem = new ArchiveItem("board", Guid.NewGuid(), boardId, "Test", userId, snapshot, null);

        _archiveItemRepoMock.Setup(r => r.GetByIdAsync(archiveItem.Id, default))
            .ReturnsAsync(archiveItem);

        var dto = new RestoreArchiveItemDto(null, RestoreMode.Copy, ConflictStrategy.Fail);
        var result = await plannerNoAuth.PlanRestoreAsync(archiveItem.Id, dto, userId);

        result.IsSuccess.Should().BeTrue();
        _authServiceMock.Verify(a => a.CanWriteBoardAsync(It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
    }
}
