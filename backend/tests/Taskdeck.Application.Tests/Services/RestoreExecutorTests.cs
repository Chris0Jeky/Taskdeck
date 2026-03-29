using System.Text.Json;
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

public class RestoreExecutorTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IBoardRepository> _boardRepoMock;
    private readonly Mock<IColumnRepository> _columnRepoMock;
    private readonly Mock<ICardRepository> _cardRepoMock;
    private readonly RestoreExecutor _executor;

    public RestoreExecutorTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _boardRepoMock = new Mock<IBoardRepository>();
        _columnRepoMock = new Mock<IColumnRepository>();
        _cardRepoMock = new Mock<ICardRepository>();

        _unitOfWorkMock.Setup(u => u.Boards).Returns(_boardRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Columns).Returns(_columnRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Cards).Returns(_cardRepoMock.Object);

        _executor = new RestoreExecutor(_unitOfWorkMock.Object);
    }

    #region Board Restore

    [Fact]
    public async Task ExecuteAsync_Board_CopyMode_CreatesNewBoard()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var snapshot = JsonSerializer.Serialize(new { Name = "My Board", Description = "Desc" });
        var archiveItem = new ArchiveItem("board", Guid.NewGuid(), boardId, "My Board", userId, snapshot, null);
        var dto = new RestoreArchiveItemDto(null, RestoreMode.Copy, ConflictStrategy.Fail);
        var plan = new RestorePlan(archiveItem, boardId, dto);

        _boardRepoMock.Setup(b => b.SearchAsync("My Board", false, default))
            .ReturnsAsync(Array.Empty<Board>());
        _boardRepoMock.Setup(b => b.AddAsync(It.IsAny<Board>(), default))
            .ReturnsAsync((Board b, CancellationToken _) => b);

        var result = await _executor.ExecuteAsync(plan, userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.RestoredEntityId.Should().NotBeNull();
        result.Value.ResolvedName.Should().Be("My Board");
        _boardRepoMock.Verify(b => b.AddAsync(It.IsAny<Board>(), default), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_Board_ConflictWithFail_ReturnsConflict()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var snapshot = JsonSerializer.Serialize(new { Name = "My Board", Description = "Desc" });
        var archiveItem = new ArchiveItem("board", Guid.NewGuid(), boardId, "My Board", userId, snapshot, null);
        var dto = new RestoreArchiveItemDto(null, RestoreMode.Copy, ConflictStrategy.Fail);
        var plan = new RestorePlan(archiveItem, boardId, dto);

        var existingBoard = new Board("My Board", null, userId);
        _boardRepoMock.Setup(b => b.SearchAsync("My Board", false, default))
            .ReturnsAsync(new[] { existingBoard });

        var result = await _executor.ExecuteAsync(plan, userId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
    }

    [Fact]
    public async Task ExecuteAsync_Board_ConflictWithRename_CreatesRenamedBoard()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var snapshot = JsonSerializer.Serialize(new { Name = "My Board", Description = "Desc" });
        var archiveItem = new ArchiveItem("board", Guid.NewGuid(), boardId, "My Board", userId, snapshot, null);
        var dto = new RestoreArchiveItemDto(null, RestoreMode.Copy, ConflictStrategy.Rename);
        var plan = new RestorePlan(archiveItem, boardId, dto);

        var existingBoard = new Board("My Board", null, userId);
        _boardRepoMock.Setup(b => b.SearchAsync("My Board", false, default))
            .ReturnsAsync(new[] { existingBoard });
        _boardRepoMock.Setup(b => b.AddAsync(It.IsAny<Board>(), default))
            .ReturnsAsync((Board b, CancellationToken _) => b);

        var result = await _executor.ExecuteAsync(plan, userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.ResolvedName.Should().Be("My Board (Restored)");
    }

    [Fact]
    public async Task ExecuteAsync_Board_InvalidSnapshot_ReturnsValidationError()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var archiveItem = new ArchiveItem("board", Guid.NewGuid(), boardId, "Test", userId, "invalid json{{{", null);
        var dto = new RestoreArchiveItemDto(null, RestoreMode.Copy, ConflictStrategy.Fail);
        var plan = new RestorePlan(archiveItem, boardId, dto);

        var result = await _executor.ExecuteAsync(plan, userId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task ExecuteAsync_Board_InPlaceMode_UnarchivesExistingBoard()
    {
        var userId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var snapshot = JsonSerializer.Serialize(new { Name = "My Board", Description = "Desc" });
        var archiveItem = new ArchiveItem("board", entityId, boardId, "My Board", userId, snapshot, null);
        var dto = new RestoreArchiveItemDto(null, RestoreMode.InPlace, ConflictStrategy.Fail);
        var plan = new RestorePlan(archiveItem, boardId, dto);

        _boardRepoMock.Setup(b => b.SearchAsync("My Board", false, default))
            .ReturnsAsync(Array.Empty<Board>());

        var existingBoard = TestDataBuilder.CreateBoard(isArchived: true);
        _boardRepoMock.Setup(b => b.GetByIdAsync(entityId, default))
            .ReturnsAsync(existingBoard);

        var result = await _executor.ExecuteAsync(plan, userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.RestoredEntityId.Should().Be(existingBoard.Id);
        existingBoard.IsArchived.Should().BeFalse();
    }

    #endregion

    #region Column Restore

    [Fact]
    public async Task ExecuteAsync_Column_BoardNotFound_ReturnsNotFound()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var snapshot = JsonSerializer.Serialize(new { Name = "To Do", Position = 0, WipLimit = (int?)null });
        var archiveItem = new ArchiveItem("column", Guid.NewGuid(), boardId, "To Do", userId, snapshot, null);
        var dto = new RestoreArchiveItemDto(null, RestoreMode.Copy, ConflictStrategy.Fail);
        var plan = new RestorePlan(archiveItem, boardId, dto);

        _boardRepoMock.Setup(b => b.GetByIdWithDetailsAsync(boardId, default))
            .ReturnsAsync((Board?)null);

        var result = await _executor.ExecuteAsync(plan, userId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task ExecuteAsync_Column_InvalidSnapshot_ReturnsValidationError()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var archiveItem = new ArchiveItem("column", Guid.NewGuid(), boardId, "Test", userId, "{bad json", null);
        var dto = new RestoreArchiveItemDto(null, RestoreMode.Copy, ConflictStrategy.Fail);
        var plan = new RestorePlan(archiveItem, boardId, dto);

        var result = await _executor.ExecuteAsync(plan, userId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    #endregion

    #region Card Restore

    [Fact]
    public async Task ExecuteAsync_Card_BoardNotFound_ReturnsNotFound()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var snapshot = JsonSerializer.Serialize(new
        {
            Title = "My Card",
            Description = (string?)null,
            DueDate = (DateTimeOffset?)null,
            IsBlocked = false,
            BlockReason = (string?)null,
            ColumnId = Guid.NewGuid()
        });
        var archiveItem = new ArchiveItem("card", Guid.NewGuid(), boardId, "My Card", userId, snapshot, null);
        var dto = new RestoreArchiveItemDto(null, RestoreMode.Copy, ConflictStrategy.Fail);
        var plan = new RestorePlan(archiveItem, boardId, dto);

        _boardRepoMock.Setup(b => b.GetByIdWithDetailsAsync(boardId, default))
            .ReturnsAsync((Board?)null);

        var result = await _executor.ExecuteAsync(plan, userId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task ExecuteAsync_Card_InvalidSnapshot_ReturnsValidationError()
    {
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var archiveItem = new ArchiveItem("card", Guid.NewGuid(), boardId, "Test", userId, "not valid json!", null);
        var dto = new RestoreArchiveItemDto(null, RestoreMode.Copy, ConflictStrategy.Fail);
        var plan = new RestorePlan(archiveItem, boardId, dto);

        var result = await _executor.ExecuteAsync(plan, userId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    #endregion

    // Note: Unknown entity type path is unreachable because ArchiveItem
    // constructor validates that entityType is "board", "column", or "card".
}
