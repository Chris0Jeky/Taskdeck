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

public class CardServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IBoardRepository> _boardRepoMock;
    private readonly Mock<IColumnRepository> _columnRepoMock;
    private readonly Mock<ICardRepository> _cardRepoMock;
    private readonly Mock<ILabelRepository> _labelRepoMock;
    private readonly CardService _service;

    public CardServiceTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _boardRepoMock = new Mock<IBoardRepository>();
        _columnRepoMock = new Mock<IColumnRepository>();
        _cardRepoMock = new Mock<ICardRepository>();
        _labelRepoMock = new Mock<ILabelRepository>();

        _unitOfWorkMock.Setup(u => u.Boards).Returns(_boardRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Columns).Returns(_columnRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Cards).Returns(_cardRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Labels).Returns(_labelRepoMock.Object);

        _service = new CardService(_unitOfWorkMock.Object);
    }

    #region CreateCardAsync Tests

    [Fact]
    public async Task CreateCardAsync_ShouldReturnSuccess_WithValidData()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "To Do");
        var dto = new CreateCardDto(board.Id, column.Id, "New Card", "Description", null, null);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);
        _columnRepoMock.Setup(r => r.GetByIdWithCardsAsync(column.Id, default))
            .ReturnsAsync(column);
        _cardRepoMock.Setup(r => r.AddAsync(It.IsAny<Card>(), default))
            .Returns(Task.CompletedTask);
        _cardRepoMock.Setup(r => r.GetByIdWithLabelsAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync((Guid id, CancellationToken ct) =>
            {
                var card = TestDataBuilder.CreateCard(board.Id, column.Id, dto.Title, dto.Description);
                return card;
            });

        // Act
        var result = await _service.CreateCardAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Title.Should().Be("New Card");
        result.Value.BoardId.Should().Be(board.Id);
        result.Value.ColumnId.Should().Be(column.Id);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task CreateCardAsync_ShouldReturnNotFound_WhenBoardDoesNotExist()
    {
        // Arrange
        var boardId = Guid.NewGuid();
        var columnId = Guid.NewGuid();
        var dto = new CreateCardDto(boardId, columnId, "Card", null, null, null);

        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default))
            .ReturnsAsync((Board?)null);

        // Act
        var result = await _service.CreateCardAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.ErrorMessage.Should().Contain("Board");
    }

    [Fact]
    public async Task CreateCardAsync_ShouldReturnNotFound_WhenColumnDoesNotExist()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var columnId = Guid.NewGuid();
        var dto = new CreateCardDto(board.Id, columnId, "Card", null, null, null);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);
        _columnRepoMock.Setup(r => r.GetByIdWithCardsAsync(columnId, default))
            .ReturnsAsync((Column?)null);

        // Act
        var result = await _service.CreateCardAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.ErrorMessage.Should().Contain("Column");
    }

    [Fact]
    public async Task CreateCardAsync_ShouldEnforceWipLimit_WhenColumnAtLimit()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "In Progress", wipLimit: 2);

        // Add 2 cards to reach WIP limit
        var card1 = TestDataBuilder.CreateCard(board.Id, column.Id, "Card 1", position: 0);
        var card2 = TestDataBuilder.CreateCard(board.Id, column.Id, "Card 2", position: 1);
        column.GetType().GetProperty("Cards")!.SetValue(column, new List<Card> { card1, card2 });

        var dto = new CreateCardDto(board.Id, column.Id, "Card 3", null, null, null);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);
        _columnRepoMock.Setup(r => r.GetByIdWithCardsAsync(column.Id, default))
            .ReturnsAsync(column);

        // Act
        var result = await _service.CreateCardAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.WipLimitExceeded);
        result.ErrorMessage.Should().Contain("WIP limit");
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task CreateCardAsync_ShouldAddLabels_WhenLabelIdsProvided()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "To Do");
        var label1 = TestDataBuilder.CreateLabel(board.Id, "Bug", "#FF0000");
        var label2 = TestDataBuilder.CreateLabel(board.Id, "Feature", "#00FF00");

        var dto = new CreateCardDto(
            board.Id,
            column.Id,
            "Card with labels",
            null,
            null,
            new List<Guid> { label1.Id, label2.Id });

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);
        _columnRepoMock.Setup(r => r.GetByIdWithCardsAsync(column.Id, default))
            .ReturnsAsync(column);
        _labelRepoMock.Setup(r => r.GetByBoardIdAsync(board.Id, default))
            .ReturnsAsync(new List<Label> { label1, label2 });
        _cardRepoMock.Setup(r => r.GetByIdWithLabelsAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync((Guid id, CancellationToken ct) =>
            {
                var card = TestDataBuilder.CreateCard(board.Id, column.Id, dto.Title);
                var cardLabel1 = TestDataBuilder.CreateCardLabel(card.Id, label1.Id);
                var cardLabel2 = TestDataBuilder.CreateCardLabel(card.Id, label2.Id);

                // Use reflection to set the Label navigation property
                cardLabel1.GetType().GetProperty("Label")!.SetValue(cardLabel1, label1);
                cardLabel2.GetType().GetProperty("Label")!.SetValue(cardLabel2, label2);

                card.AddLabel(cardLabel1);
                card.AddLabel(cardLabel2);
                return card;
            });

        // Act
        var result = await _service.CreateCardAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Labels.Should().HaveCount(2);
        result.Value.Labels.Should().Contain(l => l.Name == "Bug");
        result.Value.Labels.Should().Contain(l => l.Name == "Feature");
    }

    [Fact]
    public async Task CreateCardAsync_ShouldIgnoreLabelsNotBelongingToBoard()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "To Do");
        var validLabel = TestDataBuilder.CreateLabel(board.Id, "Backend", "#123456");
        var externalLabelId = Guid.NewGuid();

        var dto = new CreateCardDto(
            board.Id,
            column.Id,
            "Card with mixed labels",
            null,
            null,
            new List<Guid> { validLabel.Id, externalLabelId });

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);
        _columnRepoMock.Setup(r => r.GetByIdWithCardsAsync(column.Id, default))
            .ReturnsAsync(column);
        _labelRepoMock.Setup(r => r.GetByBoardIdAsync(board.Id, default))
            .ReturnsAsync(new List<Label> { validLabel });
        _cardRepoMock.Setup(r => r.GetByIdWithLabelsAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync((Guid id, CancellationToken ct) =>
            {
                var card = TestDataBuilder.CreateCard(board.Id, column.Id, dto.Title);
                var cardLabel = TestDataBuilder.CreateCardLabel(card.Id, validLabel.Id);

                cardLabel.GetType().GetProperty("Label")!.SetValue(cardLabel, validLabel);
                card.AddLabel(cardLabel);

                return card;
            });

        // Act
        var result = await _service.CreateCardAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Labels.Should().HaveCount(1);
        result.Value.Labels.Single().Name.Should().Be("Backend");
    }

    [Fact]
    public async Task CreateCardAsync_ShouldAssignPositionAtBottom()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "To Do");

        // Add existing cards
        var card1 = TestDataBuilder.CreateCard(board.Id, column.Id, "Card 1", position: 0);
        var card2 = TestDataBuilder.CreateCard(board.Id, column.Id, "Card 2", position: 1);
        column.GetType().GetProperty("Cards")!.SetValue(column, new List<Card> { card1, card2 });

        var dto = new CreateCardDto(board.Id, column.Id, "New Card", null, null, null);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);
        _columnRepoMock.Setup(r => r.GetByIdWithCardsAsync(column.Id, default))
            .ReturnsAsync(column);
        _cardRepoMock.Setup(r => r.GetByIdWithLabelsAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync((Guid id, CancellationToken ct) =>
                TestDataBuilder.CreateCard(board.Id, column.Id, dto.Title, position: 2));

        // Act
        var result = await _service.CreateCardAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Position.Should().Be(2);
    }

    [Fact]
    public async Task CreateCardAsync_ShouldReturnValidationError_WhenTitleIsEmpty()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "To Do");
        var dto = new CreateCardDto(board.Id, column.Id, "", null, null, null);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ReturnsAsync(board);
        _columnRepoMock.Setup(r => r.GetByIdWithCardsAsync(column.Id, default))
            .ReturnsAsync(column);

        // Act
        var result = await _service.CreateCardAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    #endregion

    #region UpdateCardAsync Tests

    [Fact]
    public async Task UpdateCardAsync_ShouldUpdateBasicFields()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "To Do");
        var card = TestDataBuilder.CreateCard(board.Id, column.Id, "Original Title");
        var newDueDate = DateTimeOffset.UtcNow.AddDays(7);

        var dto = new UpdateCardDto(
            Title: "Updated Title",
            Description: "Updated Description",
            DueDate: newDueDate,
            IsBlocked: null,
            BlockReason: null,
            LabelIds: null
        );

        _cardRepoMock.Setup(r => r.GetByIdWithLabelsAsync(card.Id, default))
            .ReturnsAsync(card);

        // Act
        var result = await _service.UpdateCardAsync(card.Id, dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        card.Title.Should().Be("Updated Title");
        card.Description.Should().Be("Updated Description");
        card.DueDate.Should().Be(newDueDate);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task UpdateCardAsync_ShouldBlockCard_WhenBlockedIsTrue()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "To Do");
        var card = TestDataBuilder.CreateCard(board.Id, column.Id, "Task");

        var dto = new UpdateCardDto(
            Title: null,
            Description: null,
            DueDate: null,
            IsBlocked: true,
            BlockReason: "Waiting for API",
            LabelIds: null
        );

        _cardRepoMock.Setup(r => r.GetByIdWithLabelsAsync(card.Id, default))
            .ReturnsAsync(card);

        // Act
        var result = await _service.UpdateCardAsync(card.Id, dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        card.IsBlocked.Should().BeTrue();
        card.BlockReason.Should().Be("Waiting for API");
    }

    [Fact]
    public async Task UpdateCardAsync_ShouldUnblockCard_WhenBlockedIsFalse()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "To Do");
        var card = TestDataBuilder.CreateCard(board.Id, column.Id, "Task", isBlocked: true, blockReason: "Waiting");

        var dto = new UpdateCardDto(
            Title: null,
            Description: null,
            DueDate: null,
            IsBlocked: false,
            BlockReason: null,
            LabelIds: null
        );

        _cardRepoMock.Setup(r => r.GetByIdWithLabelsAsync(card.Id, default))
            .ReturnsAsync(card);

        // Act
        var result = await _service.UpdateCardAsync(card.Id, dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        card.IsBlocked.Should().BeFalse();
        card.BlockReason.Should().BeNull();
    }

    [Fact]
    public async Task UpdateCardAsync_ShouldReplaceLabels()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "To Do");
        var card = TestDataBuilder.CreateCard(board.Id, column.Id, "Task");

        var oldLabel = TestDataBuilder.CreateLabel(board.Id, "Old", "#FF0000");
        var newLabel = TestDataBuilder.CreateLabel(board.Id, "New", "#00FF00");

        // Add old label
        var oldCardLabel = TestDataBuilder.CreateCardLabel(card.Id, oldLabel.Id);
        card.AddLabel(oldCardLabel);

        var dto = new UpdateCardDto(
            Title: null,
            Description: null,
            DueDate: null,
            IsBlocked: null,
            BlockReason: null,
            LabelIds: new List<Guid> { newLabel.Id }
        );

        _cardRepoMock.Setup(r => r.GetByIdWithLabelsAsync(card.Id, default))
            .ReturnsAsync(card);
        _labelRepoMock.Setup(r => r.GetByBoardIdAsync(board.Id, default))
            .ReturnsAsync(new List<Label> { oldLabel, newLabel });

        // Act
        var result = await _service.UpdateCardAsync(card.Id, dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        card.CardLabels.Should().HaveCount(1);
        card.CardLabels.First().LabelId.Should().Be(newLabel.Id);
    }

    [Fact]
    public async Task UpdateCardAsync_ShouldIgnoreInvalidLabelIds()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "To Do");
        var card = TestDataBuilder.CreateCard(board.Id, column.Id, "Task");

        var validLabel = TestDataBuilder.CreateLabel(board.Id, "QA", "#00FF00");
        var externalLabelId = Guid.NewGuid();

        var dto = new UpdateCardDto(
            Title: null,
            Description: null,
            DueDate: null,
            IsBlocked: null,
            BlockReason: null,
            LabelIds: new List<Guid> { validLabel.Id, externalLabelId }
        );

        _cardRepoMock.Setup(r => r.GetByIdWithLabelsAsync(card.Id, default))
            .ReturnsAsync(card);
        _labelRepoMock.Setup(r => r.GetByBoardIdAsync(board.Id, default))
            .ReturnsAsync(new List<Label> { validLabel });
        _cardRepoMock.Setup(r => r.GetByIdWithLabelsAsync(card.Id, default))
            .ReturnsAsync(card);

        // Act
        var result = await _service.UpdateCardAsync(card.Id, dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Labels.Should().HaveCount(1);
        result.Value.Labels.Single().Name.Should().Be("QA");
    }

    [Fact]
    public async Task UpdateCardAsync_ShouldReturnNotFound_WhenCardDoesNotExist()
    {
        // Arrange
        var cardId = Guid.NewGuid();
        var dto = new UpdateCardDto(
            Title: "Updated",
            Description: null,
            DueDate: null,
            IsBlocked: null,
            BlockReason: null,
            LabelIds: null
        );

        _cardRepoMock.Setup(r => r.GetByIdWithLabelsAsync(cardId, default))
            .ReturnsAsync((Card?)null);

        // Act
        var result = await _service.UpdateCardAsync(cardId, dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    #endregion

    #region MoveCardAsync Tests

    [Fact]
    public async Task MoveCardAsync_ShouldMoveCardBetweenColumns()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var sourceColumn = TestDataBuilder.CreateColumn(board.Id, "To Do", position: 0);
        var targetColumn = TestDataBuilder.CreateColumn(board.Id, "In Progress", position: 1);

        var card = TestDataBuilder.CreateCard(board.Id, sourceColumn.Id, "Task", position: 0);

        var dto = new MoveCardDto(targetColumn.Id, 0);

        _cardRepoMock.Setup(r => r.GetByIdWithLabelsAsync(card.Id, default))
            .ReturnsAsync(card);
        _columnRepoMock.Setup(r => r.GetByIdWithCardsAsync(targetColumn.Id, default))
            .ReturnsAsync(targetColumn);
        _cardRepoMock.Setup(r => r.GetByColumnIdAsync(targetColumn.Id, default))
            .ReturnsAsync(new List<Card>());

        // Act
        var result = await _service.MoveCardAsync(card.Id, dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        card.ColumnId.Should().Be(targetColumn.Id);
        card.Position.Should().Be(0);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task MoveCardAsync_ShouldEnforceWipLimit_OnTargetColumn()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var sourceColumn = TestDataBuilder.CreateColumn(board.Id, "To Do");
        var targetColumn = TestDataBuilder.CreateColumn(board.Id, "In Progress", wipLimit: 1);

        var card = TestDataBuilder.CreateCard(board.Id, sourceColumn.Id, "Task to Move");
        var existingCard = TestDataBuilder.CreateCard(board.Id, targetColumn.Id, "Existing Task");

        targetColumn.GetType().GetProperty("Cards")!.SetValue(targetColumn, new List<Card> { existingCard });

        var dto = new MoveCardDto(targetColumn.Id, 0);

        _cardRepoMock.Setup(r => r.GetByIdWithLabelsAsync(card.Id, default))
            .ReturnsAsync(card);
        _columnRepoMock.Setup(r => r.GetByIdWithCardsAsync(targetColumn.Id, default))
            .ReturnsAsync(targetColumn);

        // Act
        var result = await _service.MoveCardAsync(card.Id, dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.WipLimitExceeded);
        result.ErrorMessage.Should().Contain("WIP limit");
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task MoveCardAsync_ShouldAllowMove_WithinSameColumn()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "To Do", wipLimit: 2);

        var card1 = TestDataBuilder.CreateCard(board.Id, column.Id, "Card 1", position: 0);
        var card2 = TestDataBuilder.CreateCard(board.Id, column.Id, "Card 2", position: 1);

        column.GetType().GetProperty("Cards")!.SetValue(column, new List<Card> { card1, card2 });

        var dto = new MoveCardDto(column.Id, 1); // Move card1 to position 1

        _cardRepoMock.Setup(r => r.GetByIdWithLabelsAsync(card1.Id, default))
            .ReturnsAsync(card1);
        _columnRepoMock.Setup(r => r.GetByIdWithCardsAsync(column.Id, default))
            .ReturnsAsync(column);
        _cardRepoMock.Setup(r => r.GetByColumnIdAsync(column.Id, default))
            .ReturnsAsync(new List<Card> { card1, card2 });

        // Act
        var result = await _service.MoveCardAsync(card1.Id, dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        // WIP limit should not be enforced when moving within same column
    }

    [Fact]
    public async Task MoveCardAsync_ShouldReorderCards_InTargetColumn()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "To Do");

        var card1 = TestDataBuilder.CreateCard(board.Id, column.Id, "Card 1", position: 0);
        var card2 = TestDataBuilder.CreateCard(board.Id, column.Id, "Card 2", position: 1);
        var card3 = TestDataBuilder.CreateCard(board.Id, column.Id, "Card 3", position: 2);

        var dto = new MoveCardDto(column.Id, 1); // Move card3 to position 1

        _cardRepoMock.Setup(r => r.GetByIdWithLabelsAsync(card3.Id, default))
            .ReturnsAsync(card3);
        _columnRepoMock.Setup(r => r.GetByIdWithCardsAsync(column.Id, default))
            .ReturnsAsync(column);
        _cardRepoMock.Setup(r => r.GetByColumnIdAsync(column.Id, default))
            .ReturnsAsync(new List<Card> { card1, card2, card3 });

        // Act
        var result = await _service.MoveCardAsync(card3.Id, dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        card1.Position.Should().Be(0); // Stays at 0
        card3.Position.Should().Be(1); // Moved to 1
        card2.Position.Should().Be(2); // Pushed to 2
    }

    [Fact]
    public async Task MoveCardAsync_ShouldReturnNotFound_WhenCardDoesNotExist()
    {
        // Arrange
        var cardId = Guid.NewGuid();
        var dto = new MoveCardDto(Guid.NewGuid(), 0);

        _cardRepoMock.Setup(r => r.GetByIdWithLabelsAsync(cardId, default))
            .ReturnsAsync((Card?)null);

        // Act
        var result = await _service.MoveCardAsync(cardId, dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.ErrorMessage.Should().Contain("Card");
    }

    [Fact]
    public async Task MoveCardAsync_ShouldReturnNotFound_WhenTargetColumnDoesNotExist()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "To Do");
        var card = TestDataBuilder.CreateCard(board.Id, column.Id, "Task");
        var targetColumnId = Guid.NewGuid();

        var dto = new MoveCardDto(targetColumnId, 0);

        _cardRepoMock.Setup(r => r.GetByIdWithLabelsAsync(card.Id, default))
            .ReturnsAsync(card);
        _columnRepoMock.Setup(r => r.GetByIdWithCardsAsync(targetColumnId, default))
            .ReturnsAsync((Column?)null);

        // Act
        var result = await _service.MoveCardAsync(card.Id, dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.ErrorMessage.Should().Contain("Column");
    }

    #endregion

    #region SearchCardsAsync Tests

    [Fact]
    public async Task SearchCardsAsync_ShouldReturnAllCards_WhenNoFilters()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "To Do");
        var cards = new List<Card>
        {
            TestDataBuilder.CreateCard(board.Id, column.Id, "Card 1"),
            TestDataBuilder.CreateCard(board.Id, column.Id, "Card 2")
        };

        _cardRepoMock.Setup(r => r.SearchAsync(board.Id, null, null, null, default))
            .ReturnsAsync(cards);

        // Act
        var result = await _service.SearchCardsAsync(board.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task SearchCardsAsync_ShouldFilterByText()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "To Do");
        var matchingCard = TestDataBuilder.CreateCard(board.Id, column.Id, "Fix bug in auth");

        _cardRepoMock.Setup(r => r.SearchAsync(board.Id, "bug", null, null, default))
            .ReturnsAsync(new List<Card> { matchingCard });

        // Act
        var result = await _service.SearchCardsAsync(board.Id, searchText: "bug");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value.First().Title.Should().Contain("bug");
    }

    [Fact]
    public async Task SearchCardsAsync_ShouldFilterByLabel()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "To Do");
        var label = TestDataBuilder.CreateLabel(board.Id, "Bug");
        var card = TestDataBuilder.CreateCard(board.Id, column.Id, "Task");

        _cardRepoMock.Setup(r => r.SearchAsync(board.Id, null, label.Id, null, default))
            .ReturnsAsync(new List<Card> { card });

        // Act
        var result = await _service.SearchCardsAsync(board.Id, labelId: label.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
    }

    [Fact]
    public async Task SearchCardsAsync_ShouldFilterByColumn()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "To Do");
        var card = TestDataBuilder.CreateCard(board.Id, column.Id, "Task");

        _cardRepoMock.Setup(r => r.SearchAsync(board.Id, null, null, column.Id, default))
            .ReturnsAsync(new List<Card> { card });

        // Act
        var result = await _service.SearchCardsAsync(board.Id, columnId: column.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
    }

    #endregion

    #region DeleteCardAsync Tests

    [Fact]
    public async Task DeleteCardAsync_ShouldDeleteCard_WhenExists()
    {
        // Arrange
        var board = TestDataBuilder.CreateBoard();
        var column = TestDataBuilder.CreateColumn(board.Id, "To Do");
        var card = TestDataBuilder.CreateCard(board.Id, column.Id, "Task");

        _cardRepoMock.Setup(r => r.GetByIdAsync(card.Id, default))
            .ReturnsAsync(card);

        // Act
        var result = await _service.DeleteCardAsync(card.Id);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _cardRepoMock.Verify(r => r.DeleteAsync(card, default), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task DeleteCardAsync_ShouldReturnNotFound_WhenCardDoesNotExist()
    {
        // Arrange
        var cardId = Guid.NewGuid();

        _cardRepoMock.Setup(r => r.GetByIdAsync(cardId, default))
            .ReturnsAsync((Card?)null);

        // Act
        var result = await _service.DeleteCardAsync(cardId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        _cardRepoMock.Verify(r => r.DeleteAsync(It.IsAny<Card>(), default), Times.Never);
    }

    #endregion
}
