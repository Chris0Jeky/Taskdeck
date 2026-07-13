using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Application.Services.Pipeline;
using Taskdeck.Application.Tests.TestUtilities;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services.Pipeline;

public class OperationHandlerRegistryTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<CardService> _cardServiceMock;
    private readonly Mock<BoardService> _boardServiceMock;
    private readonly Mock<ColumnService> _columnServiceMock;
    private readonly Mock<IBoardRepository> _boardRepoMock;
    private readonly Mock<IColumnRepository> _columnRepoMock;
    private readonly Mock<ICardRepository> _cardRepoMock;
    private readonly Mock<ILabelRepository> _labelRepoMock;
    private readonly OperationHandlerRegistry _registry;

    public OperationHandlerRegistryTests()
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
        _unitOfWorkMock.Setup(u => u.SaveChangesAsync(default)).ReturnsAsync(1);

        _cardServiceMock = new Mock<CardService>(_unitOfWorkMock.Object);
        _boardServiceMock = new Mock<BoardService>(_unitOfWorkMock.Object);
        _columnServiceMock = new Mock<ColumnService>(_unitOfWorkMock.Object);

        _registry = new OperationHandlerRegistry(
            _unitOfWorkMock.Object,
            _cardServiceMock.Object,
            _boardServiceMock.Object,
            _columnServiceMock.Object);
    }

    [Fact]
    public async Task ExecuteOperationAsync_ShouldReturnFailure_ForUnsupportedTargetType()
    {
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, "create", "widget", null,
            """{"title":"Test"}""", "key1", null);

        var result = await _registry.ExecuteOperationAsync(operation, default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Unsupported target type");
    }

    [Fact]
    public async Task ExecuteOperationAsync_ShouldReturnFailure_ForUnsupportedCardAction()
    {
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, "delete", "card", null,
            """{"cardId":"some-id"}""", "key1", null);

        var result = await _registry.ExecuteOperationAsync(operation, default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Unsupported card action");
    }

    [Fact]
    public async Task ExecuteOperationAsync_ShouldReturnFailure_ForUnsupportedBoardAction()
    {
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, "delete", "board", null,
            """{"boardId":"some-id"}""", "key1", null);

        var result = await _registry.ExecuteOperationAsync(operation, default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Unsupported board action");
    }

    [Fact]
    public async Task ExecuteOperationAsync_ShouldReturnFailure_ForUnsupportedColumnAction()
    {
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, "delete", "column", null,
            """{"columnId":"some-id"}""", "key1", null);

        var result = await _registry.ExecuteOperationAsync(operation, default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Unsupported column action");
    }

    [Fact]
    public async Task ExecuteOperationAsync_ShouldReturnFailure_ForEmptyParameters()
    {
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, "create", "card", null,
            "", "key1", null);

        var result = await _registry.ExecuteOperationAsync(operation, default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task ExecuteOperationAsync_ShouldReturnFailure_ForMissingRequiredCardCreateParameters()
    {
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, "create", "card", null,
            $$"""{"title":"Test","columnId":"{{Guid.NewGuid()}}"}""", "key1", null);

        var result = await _registry.ExecuteOperationAsync(operation, default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("Missing required parameter 'boardId'");
    }

    [Fact]
    public async Task ExecuteOperationAsync_ShouldSucceed_ForValidBoardUpdate()
    {
        var board = TestDataBuilder.CreateBoard();
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, "update", "board", null,
            $$"""{"boardId":"{{board.Id}}","name":"Updated"}""", "key1", null);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default)).ReturnsAsync(board);

        var result = await _registry.ExecuteOperationAsync(operation, default);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteOperationAsync_ShouldReturnFailure_ForColumnReorderWithNegativePosition()
    {
        var columnId = Guid.NewGuid();
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, "reorder", "column", null,
            $$"""{"columnId":"{{columnId}}","position":-1}""", "key1", null);

        var result = await _registry.ExecuteOperationAsync(operation, default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("must be non-negative");
    }

    [Fact]
    public async Task ExecuteOperationAsync_ShouldReturnFailure_ForUpdateCardWithoutTitleOrDescription()
    {
        var cardId = Guid.NewGuid();
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, "update", "card", null,
            $$"""{"cardId":"{{cardId}}"}""", "key1", null);

        var result = await _registry.ExecuteOperationAsync(operation, default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("at least one of 'title', 'description', 'dueDate', 'clearDueDate', or 'labels'");
    }

    [Fact]
    public async Task ExecuteOperationAsync_ShouldCreateCardWithUtcDueDateAndResolvedLabels()
    {
        var board = TestDataBuilder.CreateBoard();
        var column = new Column(board.Id, "Inbox", 0);
        var label = new Label(board.Id, "urgent", "#FF0000");
        Card? createdCard = null;
        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default)).ReturnsAsync(board);
        _columnRepoMock.Setup(r => r.GetByIdWithCardsAsync(column.Id, default)).ReturnsAsync(column);
        _labelRepoMock.Setup(r => r.GetByBoardIdAsync(board.Id, default)).ReturnsAsync(new[] { label });
        _cardRepoMock.Setup(r => r.AddAsync(It.IsAny<Card>(), default))
            .Callback<Card, CancellationToken>((card, _) => createdCard = card)
            .ReturnsAsync((Card card, CancellationToken _) => card);
        _cardRepoMock.Setup(r => r.GetByIdWithLabelsAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync(() =>
            {
                if (createdCard != null)
                {
                    foreach (var cardLabel in createdCard.CardLabels)
                        cardLabel.Label = label;
                }

                return createdCard;
            });

        var operation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, "create", "card", null,
            $$"""{"title":"Review brief","boardId":"{{board.Id}}","columnId":"{{column.Id}}","dueDate":"2026-07-14T09:30:00+02:00","labels":["urgent"]}""",
            "create-due", null);

        var result = await _registry.ExecuteOperationAsync(operation, default);

        result.IsSuccess.Should().BeTrue();
        createdCard.Should().NotBeNull();
        createdCard!.DueDate.Should().Be(new DateTimeOffset(2026, 7, 14, 7, 30, 0, TimeSpan.Zero));
        createdCard.CardLabels.Should().ContainSingle(cardLabel => cardLabel.LabelId == label.Id);
    }

    [Fact]
    public async Task ExecuteOperationAsync_ShouldSetThenClearCardDueDate()
    {
        var boardId = Guid.NewGuid();
        var card = new Card(boardId, Guid.NewGuid(), "Review brief");
        _cardRepoMock.Setup(r => r.GetByIdWithLabelsAsync(card.Id, default)).ReturnsAsync(card);

        var setOperation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, "update", "card", card.Id.ToString(),
            $$"""{"cardId":"{{card.Id}}","dueDate":"2026-07-14"}""", "set-due", null);
        var clearOperation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 1, "update", "card", card.Id.ToString(),
            $$"""{"cardId":"{{card.Id}}","clearDueDate":true}""", "clear-due", null);

        (await _registry.ExecuteOperationAsync(setOperation, default)).IsSuccess.Should().BeTrue();
        card.DueDate.Should().Be(new DateTimeOffset(2026, 7, 14, 0, 0, 0, TimeSpan.Zero));

        (await _registry.ExecuteOperationAsync(clearOperation, default)).IsSuccess.Should().BeTrue();
        card.DueDate.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteOperationAsync_ShouldApplyLabelOperationsIdempotently()
    {
        var boardId = Guid.NewGuid();
        var card = new Card(boardId, Guid.NewGuid(), "Review brief");
        var label = new Label(boardId, "urgent", "#FF0000");
        _cardRepoMock.Setup(r => r.GetByIdWithLabelsAsync(card.Id, default)).ReturnsAsync(() =>
        {
            foreach (var cardLabel in card.CardLabels)
                cardLabel.Label = label;
            return card;
        });
        _labelRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { label });

        var addOperation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, "add-label", "card", card.Id.ToString(),
            $$"""{"cardId":"{{card.Id}}","labelName":"urgent"}""", "add-label", null);
        var removeOperation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 1, "remove-label", "card", card.Id.ToString(),
            $$"""{"cardId":"{{card.Id}}","labelId":"{{label.Id}}"}""", "remove-label", null);

        (await _registry.ExecuteOperationAsync(addOperation, default)).IsSuccess.Should().BeTrue();
        (await _registry.ExecuteOperationAsync(addOperation, default)).IsSuccess.Should().BeTrue();
        card.CardLabels.Should().ContainSingle(cardLabel => cardLabel.LabelId == label.Id);

        (await _registry.ExecuteOperationAsync(removeOperation, default)).IsSuccess.Should().BeTrue();
        (await _registry.ExecuteOperationAsync(removeOperation, default)).IsSuccess.Should().BeTrue();
        card.CardLabels.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteOperationAsync_ShouldReplaceLabelsByNameForExistingToolCallers()
    {
        var boardId = Guid.NewGuid();
        var card = new Card(boardId, Guid.NewGuid(), "Review brief");
        var oldLabel = new Label(boardId, "old", "#111111");
        var newLabel = new Label(boardId, "urgent", "#FF0000");
        card.AddLabel(new CardLabel(card.Id, oldLabel.Id));
        _cardRepoMock.Setup(r => r.GetByIdAsync(card.Id, default)).ReturnsAsync(card);
        _cardRepoMock.Setup(r => r.GetByIdWithLabelsAsync(card.Id, default)).ReturnsAsync(() =>
        {
            foreach (var cardLabel in card.CardLabels)
                cardLabel.Label = cardLabel.LabelId == newLabel.Id ? newLabel : oldLabel;
            return card;
        });
        _labelRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { oldLabel, newLabel });
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, "update", "card", card.Id.ToString(),
            $$"""{"cardId":"{{card.Id}}","labels":["urgent","URGENT"]}""", "replace-labels", null);

        var result = await _registry.ExecuteOperationAsync(operation, default);

        result.IsSuccess.Should().BeTrue();
        card.CardLabels.Should().ContainSingle(cardLabel => cardLabel.LabelId == newLabel.Id);
    }

    [Fact]
    public async Task ExecuteOperationAsync_ShouldRejectLabelFromAnotherBoard()
    {
        var boardId = Guid.NewGuid();
        var card = new Card(boardId, Guid.NewGuid(), "Review brief");
        var otherBoardLabel = new Label(Guid.NewGuid(), "urgent", "#FF0000");
        _cardRepoMock.Setup(r => r.GetByIdWithLabelsAsync(card.Id, default)).ReturnsAsync(card);
        _labelRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(Array.Empty<Label>());

        var operation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, "add-label", "card", card.Id.ToString(),
            $$"""{"cardId":"{{card.Id}}","labelId":"{{otherBoardLabel.Id}}"}""", "cross-board", null);

        var result = await _registry.ExecuteOperationAsync(operation, default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        card.CardLabels.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteOperationAsync_ShouldCatchExceptions_AndReturnFailure()
    {
        var board = TestDataBuilder.CreateBoard();
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, "update", "board", null,
            $$"""{"boardId":"{{board.Id}}","name":"Updated"}""", "key1", null);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default))
            .ThrowsAsync(new InvalidOperationException("DB error"));

        var result = await _registry.ExecuteOperationAsync(operation, default);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.UnexpectedError);
        result.ErrorMessage.Should().Contain("DB error");
    }

    [Fact]
    public async Task ExecuteOperationAsync_ShouldBeCaseInsensitive_ForTargetAndActionTypes()
    {
        var board = TestDataBuilder.CreateBoard();
        var operation = new ProposalOperationDto(
            Guid.NewGuid(), Guid.NewGuid(), 0, "Update", "BOARD", null,
            $$"""{"boardId":"{{board.Id}}","name":"Updated"}""", "key1", null);

        _boardRepoMock.Setup(r => r.GetByIdAsync(board.Id, default)).ReturnsAsync(board);

        var result = await _registry.ExecuteOperationAsync(operation, default);

        result.IsSuccess.Should().BeTrue();
    }
}
