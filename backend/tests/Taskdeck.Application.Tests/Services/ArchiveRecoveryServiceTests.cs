using System.Text.Json;
using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Application.Tests.TestUtilities;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class ArchiveRecoveryServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IArchiveItemRepository> _archiveItemRepoMock;
    private readonly Mock<IAuditLogRepository> _auditLogRepoMock;
    private readonly Mock<IBoardRepository> _boardRepoMock;
    private readonly Mock<IColumnRepository> _columnRepoMock;
    private readonly Mock<ICardRepository> _cardRepoMock;
    private readonly Mock<IAuthorizationService> _authorizationServiceMock;
    private readonly ArchiveRecoveryService _service;

    public ArchiveRecoveryServiceTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _archiveItemRepoMock = new Mock<IArchiveItemRepository>();
        _auditLogRepoMock = new Mock<IAuditLogRepository>();
        _boardRepoMock = new Mock<IBoardRepository>();
        _columnRepoMock = new Mock<IColumnRepository>();
        _cardRepoMock = new Mock<ICardRepository>();
        _authorizationServiceMock = new Mock<IAuthorizationService>();

        _unitOfWorkMock.Setup(u => u.ArchiveItems).Returns(_archiveItemRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.AuditLogs).Returns(_auditLogRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Boards).Returns(_boardRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Columns).Returns(_columnRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Cards).Returns(_cardRepoMock.Object);

        _service = new ArchiveRecoveryService(_unitOfWorkMock.Object, _authorizationServiceMock.Object);
    }

    #region CreateArchiveItemAsync Tests

    [Fact]
    public async Task CreateArchiveItemAsync_ShouldReturnSuccess_WithValidData()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var snapshotJson = JsonSerializer.Serialize(new { Name = "Test Board", Description = "Test" });
        var dto = new CreateArchiveItemDto(
            "board",
            entityId,
            boardId,
            "Test Board",
            userId,
            snapshotJson,
            "Archived by user");

        _archiveItemRepoMock.Setup(r => r.AddAsync(It.IsAny<ArchiveItem>(), default))
            .ReturnsAsync((ArchiveItem a, CancellationToken ct) => a);
        _auditLogRepoMock.Setup(r => r.AddAsync(It.IsAny<AuditLog>(), default))
            .ReturnsAsync((AuditLog a, CancellationToken ct) => a);

        // Act
        var result = await _service.CreateArchiveItemAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.EntityType.Should().Be("board");
        result.Value.EntityId.Should().Be(entityId);
        result.Value.BoardId.Should().Be(boardId);
        result.Value.Name.Should().Be("Test Board");
        result.Value.ArchivedByUserId.Should().Be(userId);
        result.Value.RestoreStatus.Should().Be(RestoreStatus.Available);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task CreateArchiveItemAsync_ShouldReturnFailure_WithInvalidEntityType()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var dto = new CreateArchiveItemDto(
            "invalid",
            entityId,
            boardId,
            "Test",
            userId,
            "{}",
            null);

        // Act
        var result = await _service.CreateArchiveItemAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("EntityType");
    }

    [Fact]
    public async Task CreateArchiveItemAsync_ShouldCreateAuditLog()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var snapshotJson = JsonSerializer.Serialize(new { Name = "Test Card" });
        var dto = new CreateArchiveItemDto(
            "card",
            entityId,
            boardId,
            "Test Card",
            userId,
            snapshotJson,
            null);

        _archiveItemRepoMock.Setup(r => r.AddAsync(It.IsAny<ArchiveItem>(), default))
            .ReturnsAsync((ArchiveItem a, CancellationToken ct) => a);
        _auditLogRepoMock.Setup(r => r.AddAsync(It.IsAny<AuditLog>(), default))
            .ReturnsAsync((AuditLog a, CancellationToken ct) => a);

        // Act
        var result = await _service.CreateArchiveItemAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _auditLogRepoMock.Verify(r => r.AddAsync(
            It.Is<AuditLog>(a => 
                a.EntityType == "ArchiveItem" 
                && a.Action == AuditAction.Created 
                && a.UserId == userId),
            default), Times.Once);
    }

    #endregion

    #region GetArchiveItemsAsync Tests

    [Fact]
    public async Task GetArchiveItemsAsync_ShouldReturnAll_WhenNoFiltersProvided()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var items = new List<ArchiveItem>
        {
            CreateArchiveItem("board", Guid.NewGuid(), boardId, "Board 1", userId),
            CreateArchiveItem("card", Guid.NewGuid(), boardId, "Card 1", userId),
            CreateArchiveItem("column", Guid.NewGuid(), boardId, "Column 1", userId)
        };

        _archiveItemRepoMock.Setup(r => r.GetAllAsync(default))
            .ReturnsAsync(items);

        // Act
        var result = await _service.GetArchiveItemsAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetArchiveItemsAsync_ShouldFilterByEntityType()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var cardItems = new List<ArchiveItem>
        {
            CreateArchiveItem("card", Guid.NewGuid(), boardId, "Card 1", userId),
            CreateArchiveItem("card", Guid.NewGuid(), boardId, "Card 2", userId)
        };

        _archiveItemRepoMock.Setup(r => r.GetByEntityTypeAsync("card", 100, default))
            .ReturnsAsync(cardItems);

        // Act
        var result = await _service.GetArchiveItemsAsync(entityType: "card");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Should().OnlyContain(i => i.EntityType == "card");
    }

    [Fact]
    public async Task GetArchiveItemsAsync_ShouldFilterByBoardId()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var items = new List<ArchiveItem>
        {
            CreateArchiveItem("card", Guid.NewGuid(), boardId, "Card 1", userId),
            CreateArchiveItem("column", Guid.NewGuid(), boardId, "Column 1", userId)
        };

        _archiveItemRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, 100, default))
            .ReturnsAsync(items);

        // Act
        var result = await _service.GetArchiveItemsAsync(boardId: boardId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Should().OnlyContain(i => i.BoardId == boardId);
    }

    [Fact]
    public async Task GetArchiveItemsAsync_ShouldFilterByStatus()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var items = new List<ArchiveItem>
        {
            CreateArchiveItem("card", Guid.NewGuid(), boardId, "Card 1", userId, RestoreStatus.Available)
        };

        _archiveItemRepoMock.Setup(r => r.GetByStatusAsync(RestoreStatus.Available, 100, default))
            .ReturnsAsync(items);

        // Act
        var result = await _service.GetArchiveItemsAsync(status: RestoreStatus.Available);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value.Should().OnlyContain(i => i.RestoreStatus == RestoreStatus.Available);
    }

    [Fact]
    public async Task GetArchiveItemsAsync_ShouldRespectLimit()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var items = Enumerable.Range(0, 150)
            .Select(i => CreateArchiveItem("card", Guid.NewGuid(), boardId, $"Card {i}", userId))
            .ToList();

        _archiveItemRepoMock.Setup(r => r.GetAllAsync(default))
            .ReturnsAsync(items);

        // Act
        var result = await _service.GetArchiveItemsAsync(limit: 50);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(50);
    }

    #endregion

    #region GetArchiveItemByIdAsync Tests

    [Fact]
    public async Task GetArchiveItemByIdAsync_ShouldReturnItem_WhenExists()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var item = CreateArchiveItem("board", Guid.NewGuid(), boardId, "Test Board", userId);

        _archiveItemRepoMock.Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);

        // Act
        var result = await _service.GetArchiveItemByIdAsync(item.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(item.Id);
        result.Value.Name.Should().Be("Test Board");
    }

    [Fact]
    public async Task GetArchiveItemByIdAsync_ShouldReturnNotFound_WhenDoesNotExist()
    {
        // Arrange
        var id = Guid.NewGuid();
        _archiveItemRepoMock.Setup(r => r.GetByIdAsync(id, default))
            .ReturnsAsync((ArchiveItem?)null);

        // Act
        var result = await _service.GetArchiveItemByIdAsync(id);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    #endregion

    #region RestoreArchiveItemAsync - General Tests

    [Fact]
    public async Task RestoreArchiveItemAsync_ShouldReturnNotFound_WhenArchiveItemDoesNotExist()
    {
        // Arrange
        var id = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var dto = new RestoreArchiveItemDto(null, RestoreMode.InPlace, ConflictStrategy.Fail);

        _archiveItemRepoMock.Setup(r => r.GetByIdAsync(id, default))
            .ReturnsAsync((ArchiveItem?)null);

        // Act
        var result = await _service.RestoreArchiveItemAsync(id, dto, userId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task RestoreArchiveItemAsync_ShouldReturnFailure_WhenAlreadyRestored()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var item = CreateArchiveItem("board", Guid.NewGuid(), boardId, "Test", userId);
        item.MarkAsRestored(userId);

        var dto = new RestoreArchiveItemDto(null, RestoreMode.InPlace, ConflictStrategy.Fail);

        _archiveItemRepoMock.Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);

        // Act
        var result = await _service.RestoreArchiveItemAsync(item.Id, dto, userId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
        result.ErrorMessage.Should().Contain("Restored");
    }

    [Fact]
    public async Task RestoreArchiveItemAsync_ShouldReturnForbidden_WhenUserLacksPermission()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var snapshotJson = JsonSerializer.Serialize(new { Name = "Test", Description = (string?)null });
        var item = CreateArchiveItem("board", Guid.NewGuid(), boardId, "Test", userId, RestoreStatus.Available, snapshotJson);
        var dto = new RestoreArchiveItemDto(null, RestoreMode.InPlace, ConflictStrategy.Fail);

        _archiveItemRepoMock.Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);
        _authorizationServiceMock.Setup(s => s.CanWriteBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Success(false));
        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default))
            .ReturnsAsync(TestDataBuilder.CreateBoard());

        // Act
        var result = await _service.RestoreArchiveItemAsync(item.Id, dto, userId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task RestoreArchiveItemAsync_ShouldReturnNotFound_WhenTargetBoardDoesNotExist()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var snapshotJson = JsonSerializer.Serialize(new { Name = "Test" });
        var item = CreateArchiveItem("column", Guid.NewGuid(), boardId, "Test", userId, RestoreStatus.Available, snapshotJson);
        var dto = new RestoreArchiveItemDto(boardId, RestoreMode.Copy, ConflictStrategy.Fail);

        _archiveItemRepoMock.Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);
        _authorizationServiceMock.Setup(s => s.CanWriteBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Success(true));
        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default))
            .ReturnsAsync((Board?)null);

        // Act
        var result = await _service.RestoreArchiveItemAsync(item.Id, dto, userId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.ErrorMessage.Should().Contain("board");
    }

    [Fact]
    public async Task RestoreArchiveItemAsync_ShouldReturnFailure_WhenTargetBoardIsArchived()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var archivedBoard = TestDataBuilder.CreateBoard(isArchived: true);
        var snapshotJson = JsonSerializer.Serialize(new { Name = "Test" });
        var item = CreateArchiveItem("card", Guid.NewGuid(), boardId, "Test", userId, RestoreStatus.Available, snapshotJson);
        var dto = new RestoreArchiveItemDto(archivedBoard.Id, RestoreMode.Copy, ConflictStrategy.Fail);

        _archiveItemRepoMock.Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);
        _authorizationServiceMock.Setup(s => s.CanWriteBoardAsync(userId, archivedBoard.Id))
            .ReturnsAsync(Result.Success(true));
        _boardRepoMock.Setup(r => r.GetByIdAsync(archivedBoard.Id, default))
            .ReturnsAsync(archivedBoard);

        // Act
        var result = await _service.RestoreArchiveItemAsync(item.Id, dto, userId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
        result.ErrorMessage.Should().Contain("archived board");
    }

    #endregion

    #region RestoreArchiveItemAsync - Board Tests

    [Fact]
    public async Task RestoreArchiveItemAsync_ShouldRestoreBoard_WithoutConflict()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var snapshotJson = JsonSerializer.Serialize(new { Name = "Unique Board", Description = "Test board" });
        var item = CreateArchiveItem("board", boardId, boardId, "Unique Board", userId, RestoreStatus.Available, snapshotJson);
        var dto = new RestoreArchiveItemDto(null, RestoreMode.Copy, ConflictStrategy.Fail);

        _archiveItemRepoMock.Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);
        _authorizationServiceMock.Setup(s => s.CanWriteBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Success(true));
        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default))
            .ReturnsAsync(TestDataBuilder.CreateBoard());
        _boardRepoMock.Setup(r => r.SearchAsync("Unique Board", false, default))
            .ReturnsAsync(new List<Board>());
        _boardRepoMock.Setup(r => r.AddAsync(It.IsAny<Board>(), default))
            .ReturnsAsync((Board b, CancellationToken ct) => b);
        _auditLogRepoMock.Setup(r => r.AddAsync(It.IsAny<AuditLog>(), default))
            .ReturnsAsync((AuditLog a, CancellationToken ct) => a);

        // Act
        var result = await _service.RestoreArchiveItemAsync(item.Id, dto, userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Success.Should().BeTrue();
        result.Value.RestoredEntityId.Should().NotBeEmpty();
        result.Value.ResolvedName.Should().Be("Unique Board");
        _boardRepoMock.Verify(r => r.AddAsync(It.Is<Board>(b => b.Name == "Unique Board"), default), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task RestoreArchiveItemAsync_ShouldFailOnConflict_WithFailStrategy()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var existingBoard = TestDataBuilder.CreateBoard("Existing Board");
        var snapshotJson = JsonSerializer.Serialize(new { Name = "Existing Board", Description = (string?)null });
        var item = CreateArchiveItem("board", boardId, boardId, "Existing Board", userId, RestoreStatus.Available, snapshotJson);
        var dto = new RestoreArchiveItemDto(null, RestoreMode.Copy, ConflictStrategy.Fail);

        _archiveItemRepoMock.Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);
        _authorizationServiceMock.Setup(s => s.CanWriteBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Success(true));
        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default))
            .ReturnsAsync(TestDataBuilder.CreateBoard());
        _boardRepoMock.Setup(r => r.SearchAsync("Existing Board", false, default))
            .ReturnsAsync(new List<Board> { existingBoard });

        // Act
        var result = await _service.RestoreArchiveItemAsync(item.Id, dto, userId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.ErrorMessage.Should().Contain("already exists");
    }

    [Fact]
    public async Task RestoreArchiveItemAsync_ShouldRenameOnConflict_WithRenameStrategy()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var existingBoard = TestDataBuilder.CreateBoard("Existing Board");
        var snapshotJson = JsonSerializer.Serialize(new { Name = "Existing Board", Description = (string?)null });
        var item = CreateArchiveItem("board", boardId, boardId, "Existing Board", userId, RestoreStatus.Available, snapshotJson);
        var dto = new RestoreArchiveItemDto(null, RestoreMode.Copy, ConflictStrategy.Rename);

        _archiveItemRepoMock.Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);
        _authorizationServiceMock.Setup(s => s.CanWriteBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Success(true));
        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default))
            .ReturnsAsync(TestDataBuilder.CreateBoard());
        _boardRepoMock.Setup(r => r.SearchAsync("Existing Board", false, default))
            .ReturnsAsync(new List<Board> { existingBoard });
        _boardRepoMock.Setup(r => r.AddAsync(It.IsAny<Board>(), default))
            .ReturnsAsync((Board b, CancellationToken ct) => b);
        _auditLogRepoMock.Setup(r => r.AddAsync(It.IsAny<AuditLog>(), default))
            .ReturnsAsync((AuditLog a, CancellationToken ct) => a);

        // Act
        var result = await _service.RestoreArchiveItemAsync(item.Id, dto, userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ResolvedName.Should().Be("Existing Board (Restored)");
        _boardRepoMock.Verify(r => r.AddAsync(It.Is<Board>(b => b.Name == "Existing Board (Restored)"), default), Times.Once);
    }

    [Fact]
    public async Task RestoreArchiveItemAsync_ShouldAppendSuffixOnConflict_WithAppendSuffixStrategy()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var existingBoard = TestDataBuilder.CreateBoard("Existing Board");
        var snapshotJson = JsonSerializer.Serialize(new { Name = "Existing Board", Description = (string?)null });
        var item = CreateArchiveItem("board", boardId, boardId, "Existing Board", userId, RestoreStatus.Available, snapshotJson);
        var dto = new RestoreArchiveItemDto(null, RestoreMode.Copy, ConflictStrategy.AppendSuffix);

        _archiveItemRepoMock.Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);
        _authorizationServiceMock.Setup(s => s.CanWriteBoardAsync(userId, boardId))
            .ReturnsAsync(Result.Success(true));
        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default))
            .ReturnsAsync(TestDataBuilder.CreateBoard());
        _boardRepoMock.Setup(r => r.SearchAsync("Existing Board", false, default))
            .ReturnsAsync(new List<Board> { existingBoard });
        _boardRepoMock.Setup(r => r.AddAsync(It.IsAny<Board>(), default))
            .ReturnsAsync((Board b, CancellationToken ct) => b);
        _auditLogRepoMock.Setup(r => r.AddAsync(It.IsAny<AuditLog>(), default))
            .ReturnsAsync((AuditLog a, CancellationToken ct) => a);

        // Act
        var result = await _service.RestoreArchiveItemAsync(item.Id, dto, userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ResolvedName.Should().StartWith("Existing Board - ");
        result.Value.ResolvedName.Should().MatchRegex(@"Existing Board - \d{8}-\d{6}");
    }

    #endregion

    #region RestoreArchiveItemAsync - Column Tests

    [Fact]
    public async Task RestoreArchiveItemAsync_ShouldRestoreColumn_WithoutConflict()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var board = TestDataBuilder.CreateBoard();
        var column1 = TestDataBuilder.CreateColumn(board.Id, "Existing Column", position: 0);
        var boardWithColumns = TestDataBuilder.CreateBoardWithColumns(board.Name, new[] { column1 });
        
        var snapshotJson = JsonSerializer.Serialize(new { Name = "New Column", Position = 0, WipLimit = (int?)5 });
        var item = CreateArchiveItem("column", Guid.NewGuid(), boardId, "New Column", userId, RestoreStatus.Available, snapshotJson);
        var dto = new RestoreArchiveItemDto(boardWithColumns.Id, RestoreMode.Copy, ConflictStrategy.Fail);

        _archiveItemRepoMock.Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);
        _authorizationServiceMock.Setup(s => s.CanWriteBoardAsync(userId, boardWithColumns.Id))
            .ReturnsAsync(Result.Success(true));
        _boardRepoMock.Setup(r => r.GetByIdAsync(boardWithColumns.Id, default))
            .ReturnsAsync(boardWithColumns);
        _boardRepoMock.Setup(r => r.GetByIdWithDetailsAsync(boardWithColumns.Id, default))
            .ReturnsAsync(boardWithColumns);
        _columnRepoMock.Setup(r => r.AddAsync(It.IsAny<Column>(), default))
            .ReturnsAsync((Column c, CancellationToken ct) => c);
        _auditLogRepoMock.Setup(r => r.AddAsync(It.IsAny<AuditLog>(), default))
            .ReturnsAsync((AuditLog a, CancellationToken ct) => a);

        // Act
        var result = await _service.RestoreArchiveItemAsync(item.Id, dto, userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Success.Should().BeTrue();
        result.Value.ResolvedName.Should().Be("New Column");
        _columnRepoMock.Verify(r => r.AddAsync(
            It.Is<Column>(c => c.Name == "New Column" && c.Position == 1 && c.WipLimit == 5), 
            default), Times.Once);
    }

    [Fact]
    public async Task RestoreArchiveItemAsync_ShouldRenameColumn_OnConflict()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var board = TestDataBuilder.CreateBoard();
        var column1 = TestDataBuilder.CreateColumn(board.Id, "Existing Column", position: 0);
        var boardWithColumns = TestDataBuilder.CreateBoardWithColumns(board.Name, new[] { column1 });
        
        var snapshotJson = JsonSerializer.Serialize(new { Name = "Existing Column", Position = 0, WipLimit = (int?)null });
        var item = CreateArchiveItem("column", Guid.NewGuid(), boardId, "Existing Column", userId, RestoreStatus.Available, snapshotJson);
        var dto = new RestoreArchiveItemDto(boardWithColumns.Id, RestoreMode.Copy, ConflictStrategy.Rename);

        _archiveItemRepoMock.Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);
        _authorizationServiceMock.Setup(s => s.CanWriteBoardAsync(userId, boardWithColumns.Id))
            .ReturnsAsync(Result.Success(true));
        _boardRepoMock.Setup(r => r.GetByIdAsync(boardWithColumns.Id, default))
            .ReturnsAsync(boardWithColumns);
        _boardRepoMock.Setup(r => r.GetByIdWithDetailsAsync(boardWithColumns.Id, default))
            .ReturnsAsync(boardWithColumns);
        _columnRepoMock.Setup(r => r.AddAsync(It.IsAny<Column>(), default))
            .ReturnsAsync((Column c, CancellationToken ct) => c);
        _auditLogRepoMock.Setup(r => r.AddAsync(It.IsAny<AuditLog>(), default))
            .ReturnsAsync((AuditLog a, CancellationToken ct) => a);

        // Act
        var result = await _service.RestoreArchiveItemAsync(item.Id, dto, userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ResolvedName.Should().Be("Existing Column (Restored)");
        _columnRepoMock.Verify(r => r.AddAsync(
            It.Is<Column>(c => c.Name == "Existing Column (Restored)"), 
            default), Times.Once);
    }

    #endregion

    #region RestoreArchiveItemAsync - Card Tests

    [Fact]
    public async Task RestoreArchiveItemAsync_ShouldRestoreCard_WithoutConflict()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "To Do", position: 0);
        var boardWithColumns = TestDataBuilder.CreateBoardWithColumns(board.Name, new[] { column });
        
        var snapshotJson = JsonSerializer.Serialize(new
        {
            Title = "New Card",
            Description = "Test card",
            DueDate = (DateTimeOffset?)null,
            IsBlocked = false,
            BlockReason = (string?)null,
            ColumnId = column.Id
        });
        var item = CreateArchiveItem("card", Guid.NewGuid(), boardId, "New Card", userId, RestoreStatus.Available, snapshotJson);
        var dto = new RestoreArchiveItemDto(boardWithColumns.Id, RestoreMode.Copy, ConflictStrategy.Fail);

        _archiveItemRepoMock.Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);
        _authorizationServiceMock.Setup(s => s.CanWriteBoardAsync(userId, boardWithColumns.Id))
            .ReturnsAsync(Result.Success(true));
        _boardRepoMock.Setup(r => r.GetByIdAsync(boardWithColumns.Id, default))
            .ReturnsAsync(boardWithColumns);
        _boardRepoMock.Setup(r => r.GetByIdWithDetailsAsync(boardWithColumns.Id, default))
            .ReturnsAsync(boardWithColumns);
        _columnRepoMock.Setup(r => r.GetByIdWithCardsAsync(column.Id, default))
            .ReturnsAsync(column);
        _cardRepoMock.Setup(r => r.AddAsync(It.IsAny<Card>(), default))
            .ReturnsAsync((Card c, CancellationToken ct) => c);
        _auditLogRepoMock.Setup(r => r.AddAsync(It.IsAny<AuditLog>(), default))
            .ReturnsAsync((AuditLog a, CancellationToken ct) => a);

        // Act
        var result = await _service.RestoreArchiveItemAsync(item.Id, dto, userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Success.Should().BeTrue();
        result.Value.ResolvedName.Should().Be("New Card");
        _cardRepoMock.Verify(r => r.AddAsync(
            It.Is<Card>(c => c.Title == "New Card" && c.ColumnId == column.Id), 
            default), Times.Once);
    }

    [Fact]
    public async Task RestoreArchiveItemAsync_ShouldRestoreCardToFirstColumn_WhenOriginalColumnMissing()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "To Do", position: 0);
        var boardWithColumns = TestDataBuilder.CreateBoardWithColumns(board.Name, new[] { column });
        
        var snapshotJson = JsonSerializer.Serialize(new
        {
            Title = "Card",
            Description = (string?)null,
            DueDate = (DateTimeOffset?)null,
            IsBlocked = false,
            BlockReason = (string?)null,
            ColumnId = Guid.NewGuid() // Non-existent column
        });
        var item = CreateArchiveItem("card", Guid.NewGuid(), boardId, "Card", userId, RestoreStatus.Available, snapshotJson);
        var dto = new RestoreArchiveItemDto(boardWithColumns.Id, RestoreMode.Copy, ConflictStrategy.Fail);

        _archiveItemRepoMock.Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);
        _authorizationServiceMock.Setup(s => s.CanWriteBoardAsync(userId, boardWithColumns.Id))
            .ReturnsAsync(Result.Success(true));
        _boardRepoMock.Setup(r => r.GetByIdAsync(boardWithColumns.Id, default))
            .ReturnsAsync(boardWithColumns);
        _boardRepoMock.Setup(r => r.GetByIdWithDetailsAsync(boardWithColumns.Id, default))
            .ReturnsAsync(boardWithColumns);
        _columnRepoMock.Setup(r => r.GetByIdWithCardsAsync(column.Id, default))
            .ReturnsAsync(column);
        _cardRepoMock.Setup(r => r.AddAsync(It.IsAny<Card>(), default))
            .ReturnsAsync((Card c, CancellationToken ct) => c);
        _auditLogRepoMock.Setup(r => r.AddAsync(It.IsAny<AuditLog>(), default))
            .ReturnsAsync((AuditLog a, CancellationToken ct) => a);

        // Act
        var result = await _service.RestoreArchiveItemAsync(item.Id, dto, userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _cardRepoMock.Verify(r => r.AddAsync(
            It.Is<Card>(c => c.ColumnId == column.Id), 
            default), Times.Once);
    }

    [Fact]
    public async Task RestoreArchiveItemAsync_ShouldFail_WhenTargetBoardHasNoColumns()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var board = TestDataBuilder.CreateBoard();
        
        var snapshotJson = JsonSerializer.Serialize(new
        {
            Title = "Card",
            Description = (string?)null,
            DueDate = (DateTimeOffset?)null,
            IsBlocked = false,
            BlockReason = (string?)null,
            ColumnId = Guid.NewGuid()
        });
        var item = CreateArchiveItem("card", Guid.NewGuid(), boardId, "Card", userId, RestoreStatus.Available, snapshotJson);
        var dto = new RestoreArchiveItemDto(board.Id, RestoreMode.Copy, ConflictStrategy.Fail);

        _archiveItemRepoMock.Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);
        _authorizationServiceMock.Setup(s => s.CanWriteBoardAsync(userId, board.Id))
            .ReturnsAsync(Result.Success(true));
        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);
        _boardRepoMock.Setup(r => r.GetByIdWithDetailsAsync(board.Id, default))
            .ReturnsAsync(board);

        // Act
        var result = await _service.RestoreArchiveItemAsync(item.Id, dto, userId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
        result.ErrorMessage.Should().Contain("no columns");
    }

    [Fact]
    public async Task RestoreArchiveItemAsync_ShouldFail_WhenWipLimitExceeded()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var board = TestDataBuilder.CreateBoard();
        var card1 = TestDataBuilder.CreateCard(board.Id, Guid.NewGuid(), "Card 1", position: 0);
        var card2 = TestDataBuilder.CreateCard(board.Id, Guid.NewGuid(), "Card 2", position: 1);
        var column = TestDataBuilder.CreateColumnWithCards(board.Id, "To Do", new[] { card1, card2 }, position: 0, wipLimit: 2);
        var boardWithColumns = TestDataBuilder.CreateBoardWithColumns(board.Name, new[] { column });
        
        var snapshotJson = JsonSerializer.Serialize(new
        {
            Title = "New Card",
            Description = (string?)null,
            DueDate = (DateTimeOffset?)null,
            IsBlocked = false,
            BlockReason = (string?)null,
            ColumnId = column.Id
        });
        var item = CreateArchiveItem("card", Guid.NewGuid(), boardId, "New Card", userId, RestoreStatus.Available, snapshotJson);
        var dto = new RestoreArchiveItemDto(boardWithColumns.Id, RestoreMode.Copy, ConflictStrategy.Fail);

        _archiveItemRepoMock.Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);
        _authorizationServiceMock.Setup(s => s.CanWriteBoardAsync(userId, boardWithColumns.Id))
            .ReturnsAsync(Result.Success(true));
        _boardRepoMock.Setup(r => r.GetByIdAsync(boardWithColumns.Id, default))
            .ReturnsAsync(boardWithColumns);
        _boardRepoMock.Setup(r => r.GetByIdWithDetailsAsync(boardWithColumns.Id, default))
            .ReturnsAsync(boardWithColumns);
        _columnRepoMock.Setup(r => r.GetByIdWithCardsAsync(column.Id, default))
            .ReturnsAsync(column);

        // Act
        var result = await _service.RestoreArchiveItemAsync(item.Id, dto, userId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.WipLimitExceeded);
    }

    [Fact]
    public async Task RestoreArchiveItemAsync_ShouldRestoreBlockedCard()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "To Do", position: 0);
        var boardWithColumns = TestDataBuilder.CreateBoardWithColumns(board.Name, new[] { column });
        
        var snapshotJson = JsonSerializer.Serialize(new
        {
            Title = "Blocked Card",
            Description = "Test",
            DueDate = (DateTimeOffset?)null,
            IsBlocked = true,
            BlockReason = "Waiting on dependency",
            ColumnId = column.Id
        });
        var item = CreateArchiveItem("card", Guid.NewGuid(), boardId, "Blocked Card", userId, RestoreStatus.Available, snapshotJson);
        var dto = new RestoreArchiveItemDto(boardWithColumns.Id, RestoreMode.Copy, ConflictStrategy.Fail);

        _archiveItemRepoMock.Setup(r => r.GetByIdAsync(item.Id, default))
            .ReturnsAsync(item);
        _authorizationServiceMock.Setup(s => s.CanWriteBoardAsync(userId, boardWithColumns.Id))
            .ReturnsAsync(Result.Success(true));
        _boardRepoMock.Setup(r => r.GetByIdAsync(boardWithColumns.Id, default))
            .ReturnsAsync(boardWithColumns);
        _boardRepoMock.Setup(r => r.GetByIdWithDetailsAsync(boardWithColumns.Id, default))
            .ReturnsAsync(boardWithColumns);
        _columnRepoMock.Setup(r => r.GetByIdWithCardsAsync(column.Id, default))
            .ReturnsAsync(column);
        _cardRepoMock.Setup(r => r.AddAsync(It.IsAny<Card>(), default))
            .ReturnsAsync((Card c, CancellationToken ct) => c);
        _auditLogRepoMock.Setup(r => r.AddAsync(It.IsAny<AuditLog>(), default))
            .ReturnsAsync((AuditLog a, CancellationToken ct) => a);

        // Act
        var result = await _service.RestoreArchiveItemAsync(item.Id, dto, userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _cardRepoMock.Verify(r => r.AddAsync(
            It.Is<Card>(c => c.IsBlocked && c.BlockReason == "Waiting on dependency"), 
            default), Times.Once);
    }

    #endregion

    #region Helper Methods

    private static ArchiveItem CreateArchiveItem(
        string entityType,
        Guid entityId,
        Guid boardId,
        string name,
        Guid userId,
        RestoreStatus status = RestoreStatus.Available,
        string? snapshotJson = null)
    {
        snapshotJson ??= JsonSerializer.Serialize(new { Name = name });
        var item = new ArchiveItem(entityType, entityId, boardId, name, userId, snapshotJson, null);
        
        if (status == RestoreStatus.Restored)
        {
            item.MarkAsRestored(userId);
        }
        else if (status == RestoreStatus.Expired)
        {
            item.MarkAsExpired();
        }
        else if (status == RestoreStatus.Conflict)
        {
            item.MarkAsConflict();
        }
        
        return item;
    }

    #endregion
}
