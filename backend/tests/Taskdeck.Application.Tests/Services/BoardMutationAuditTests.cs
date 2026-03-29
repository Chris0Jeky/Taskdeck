using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Application.Tests.TestUtilities;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

/// <summary>
/// Verifies that board mutation services (Card, Column, Board, Label) record
/// audit log entries via IHistoryService after successful mutations.
/// </summary>
public class BoardMutationAuditTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IBoardRepository> _boardRepoMock;
    private readonly Mock<IColumnRepository> _columnRepoMock;
    private readonly Mock<ICardRepository> _cardRepoMock;
    private readonly Mock<ILabelRepository> _labelRepoMock;
    private readonly Mock<IAuditLogRepository> _auditLogRepoMock;
    private readonly Mock<IHistoryService> _historyServiceMock;

    public BoardMutationAuditTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _boardRepoMock = new Mock<IBoardRepository>();
        _columnRepoMock = new Mock<IColumnRepository>();
        _cardRepoMock = new Mock<ICardRepository>();
        _labelRepoMock = new Mock<ILabelRepository>();
        _auditLogRepoMock = new Mock<IAuditLogRepository>();
        _historyServiceMock = new Mock<IHistoryService>();

        _unitOfWorkMock.Setup(u => u.Boards).Returns(_boardRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Columns).Returns(_columnRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Cards).Returns(_cardRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Labels).Returns(_labelRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.AuditLogs).Returns(_auditLogRepoMock.Object);

        _historyServiceMock
            .Setup(h => h.LogActionAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<AuditAction>(),
                It.IsAny<Guid?>(), It.IsAny<string?>()))
            .ReturnsAsync(Result.Success());
    }

    #region CardService Audit Tests

    [Fact]
    public async Task CreateCard_RecordsAuditLog()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "To Do");
        var dto = new CreateCardDto(board.Id, column.Id, "New Card", "Description", null, null);
        var service = new CardService(_unitOfWorkMock.Object, realtimeNotifier: null, historyService: _historyServiceMock.Object);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default)).ReturnsAsync(board);
        _columnRepoMock.Setup(r => r.GetByIdWithCardsAsync(column.Id, default)).ReturnsAsync(column);
        _cardRepoMock.Setup(r => r.AddAsync(It.IsAny<Card>(), default))
            .ReturnsAsync((Card c, CancellationToken ct) => c);
        _cardRepoMock.Setup(r => r.GetByIdWithLabelsAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync((Guid id, CancellationToken ct) =>
                TestDataBuilder.CreateCard(board.Id, column.Id, dto.Title, dto.Description));

        // Act
        var result = await service.CreateCardAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _historyServiceMock.Verify(
            h => h.LogActionAsync("card", It.IsAny<Guid>(), AuditAction.Created, null, It.Is<string?>(s => s != null && s.Contains("New Card"))),
            Times.Once);
    }

    [Fact]
    public async Task UpdateCard_RecordsAuditLog()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "To Do");
        var card = TestDataBuilder.CreateCard(board.Id, column.Id, "Card", "Desc");
        var dto = new UpdateCardDto("Updated", null, null, null, null, null, null);
        var service = new CardService(_unitOfWorkMock.Object, realtimeNotifier: null, historyService: _historyServiceMock.Object);

        _cardRepoMock.Setup(r => r.GetByIdWithLabelsAsync(card.Id, default)).ReturnsAsync(card);
        _cardRepoMock.Setup(r => r.GetByIdAsync(card.Id, default)).ReturnsAsync(card);

        // Act
        var result = await service.UpdateCardAsync(card.Id, dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _historyServiceMock.Verify(
            h => h.LogActionAsync("card", card.Id, AuditAction.Updated, It.IsAny<Guid?>(), It.IsAny<string?>()),
            Times.Once);
    }

    [Fact]
    public async Task MoveCard_RecordsAuditLog()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var sourceColumn = TestDataBuilder.CreateColumn(board.Id, "To Do");
        var targetColumn = TestDataBuilder.CreateColumn(board.Id, "In Progress", position: 1);
        var card = TestDataBuilder.CreateCard(board.Id, sourceColumn.Id, "Card");
        var dto = new MoveCardDto(targetColumn.Id, 0);
        var service = new CardService(_unitOfWorkMock.Object, realtimeNotifier: null, historyService: _historyServiceMock.Object);

        _cardRepoMock.Setup(r => r.GetByIdWithLabelsAsync(card.Id, default)).ReturnsAsync(card);
        _columnRepoMock.Setup(r => r.GetByIdWithCardsAsync(targetColumn.Id, default)).ReturnsAsync(targetColumn);
        _cardRepoMock.Setup(r => r.GetByColumnIdAsync(targetColumn.Id, default))
            .ReturnsAsync(new List<Card>());

        // Act
        var result = await service.MoveCardAsync(card.Id, dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _historyServiceMock.Verify(
            h => h.LogActionAsync("card", card.Id, AuditAction.Moved, null, It.Is<string?>(s => s != null && s.Contains("target_column"))),
            Times.Once);
    }

    [Fact]
    public async Task DeleteCard_RecordsAuditLog()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "To Do");
        var card = TestDataBuilder.CreateCard(board.Id, column.Id, "Card");
        var service = new CardService(_unitOfWorkMock.Object, realtimeNotifier: null, historyService: _historyServiceMock.Object);

        _cardRepoMock.Setup(r => r.GetByIdAsync(card.Id, default)).ReturnsAsync(card);

        // Act
        var result = await service.DeleteCardAsync(card.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _historyServiceMock.Verify(
            h => h.LogActionAsync("card", card.Id, AuditAction.Deleted, null, It.Is<string?>(s => s != null && s.Contains("Card"))),
            Times.Once);
    }

    #endregion

    #region ColumnService Audit Tests

    [Fact]
    public async Task CreateColumn_RecordsAuditLog()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var dto = new CreateColumnDto(board.Id, "New Column", null, null);
        var service = new ColumnService(_unitOfWorkMock.Object, realtimeNotifier: null, historyService: _historyServiceMock.Object);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default)).ReturnsAsync(board);
        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(board.Id, default)).ReturnsAsync(new List<Column>());

        // Act
        var result = await service.CreateColumnAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _historyServiceMock.Verify(
            h => h.LogActionAsync("column", It.IsAny<Guid>(), AuditAction.Created, null, It.Is<string?>(s => s != null && s.Contains("New Column"))),
            Times.Once);
    }

    [Fact]
    public async Task DeleteColumn_RecordsAuditLog()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "Empty Column");
        var service = new ColumnService(_unitOfWorkMock.Object, realtimeNotifier: null, historyService: _historyServiceMock.Object);

        _columnRepoMock.Setup(r => r.GetByIdWithCardsAsync(column.Id, default)).ReturnsAsync(column);

        // Act
        var result = await service.DeleteColumnAsync(column.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _historyServiceMock.Verify(
            h => h.LogActionAsync("column", column.Id, AuditAction.Deleted, null, It.Is<string?>(s => s != null && s.Contains("Empty Column"))),
            Times.Once);
    }

    #endregion

    #region BoardService Audit Tests

    [Fact]
    public async Task CreateBoard_RecordsAuditLog()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var dto = new CreateBoardDto("My Board", "Description");
        var service = new BoardService(
            _unitOfWorkMock.Object,
            authorizationService: null,
            realtimeNotifier: null,
            historyService: _historyServiceMock.Object);

        // Act
        var result = await service.CreateBoardAsync(dto, userId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _historyServiceMock.Verify(
            h => h.LogActionAsync("board", It.IsAny<Guid>(), AuditAction.Created, userId, It.Is<string?>(s => s != null && s.Contains("My Board"))),
            Times.Once);
    }

    [Fact]
    public async Task UpdateBoard_RecordsAuditLog()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var dto = new UpdateBoardDto("Renamed", null, null);
        var service = new BoardService(
            _unitOfWorkMock.Object,
            authorizationService: null,
            realtimeNotifier: null,
            historyService: _historyServiceMock.Object);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default)).ReturnsAsync(board);

        // Act
        var result = await service.UpdateBoardAsync(board.Id, dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _historyServiceMock.Verify(
            h => h.LogActionAsync("board", board.Id, AuditAction.Updated, null, It.IsAny<string?>()),
            Times.Once);
    }

    [Fact]
    public async Task DeleteBoard_RecordsArchiveAuditLog()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var service = new BoardService(
            _unitOfWorkMock.Object,
            authorizationService: null,
            realtimeNotifier: null,
            historyService: _historyServiceMock.Object);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default)).ReturnsAsync(board);

        // Act
        var result = await service.DeleteBoardAsync(board.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _historyServiceMock.Verify(
            h => h.LogActionAsync("board", board.Id, AuditAction.Archived, null, It.Is<string?>(s => s != null && s.Contains("Test Board"))),
            Times.Once);
    }

    #endregion

    #region LabelService Audit Tests

    [Fact]
    public async Task CreateLabel_RecordsAuditLog()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var dto = new CreateLabelDto(board.Id, "Urgent", "#FF0000");
        var service = new LabelService(_unitOfWorkMock.Object, realtimeNotifier: null, historyService: _historyServiceMock.Object);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default)).ReturnsAsync(board);

        // Act
        var result = await service.CreateLabelAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _historyServiceMock.Verify(
            h => h.LogActionAsync("label", It.IsAny<Guid>(), AuditAction.Created, null, It.Is<string?>(s => s != null && s.Contains("Urgent"))),
            Times.Once);
    }

    [Fact]
    public async Task DeleteLabel_RecordsAuditLog()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var label = TestDataBuilder.CreateLabel(board.Id, "Bug", "#FF0000");
        var service = new LabelService(_unitOfWorkMock.Object, realtimeNotifier: null, historyService: _historyServiceMock.Object);

        _labelRepoMock.Setup(r => r.GetByIdAsync(label.Id, default)).ReturnsAsync(label);

        // Act
        var result = await service.DeleteLabelAsync(label.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _historyServiceMock.Verify(
            h => h.LogActionAsync("label", label.Id, AuditAction.Deleted, null, It.Is<string?>(s => s != null && s.Contains("Bug"))),
            Times.Once);
    }

    #endregion

    #region No History Service (backward compatibility)

    [Fact]
    public async Task CreateCard_WithoutHistoryService_StillSucceeds()
    {
        // Arrange - use constructor without IHistoryService
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "To Do");
        var dto = new CreateCardDto(board.Id, column.Id, "Card", null, null, null);
        var service = new CardService(_unitOfWorkMock.Object);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default)).ReturnsAsync(board);
        _columnRepoMock.Setup(r => r.GetByIdWithCardsAsync(column.Id, default)).ReturnsAsync(column);
        _cardRepoMock.Setup(r => r.AddAsync(It.IsAny<Card>(), default))
            .ReturnsAsync((Card c, CancellationToken ct) => c);
        _cardRepoMock.Setup(r => r.GetByIdWithLabelsAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync((Guid id, CancellationToken ct) =>
                TestDataBuilder.CreateCard(board.Id, column.Id, dto.Title));

        // Act
        var result = await service.CreateCardAsync(dto);

        // Assert - succeeds without audit (backward compat)
        result.IsSuccess.Should().BeTrue();
    }

    #endregion
}
