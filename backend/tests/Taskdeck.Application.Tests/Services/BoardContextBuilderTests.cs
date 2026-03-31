using FluentAssertions;
using Moq;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Entities;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class BoardContextBuilderTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IBoardRepository> _boardRepoMock = new();
    private readonly Mock<IColumnRepository> _columnRepoMock = new();
    private readonly Mock<ICardRepository> _cardRepoMock = new();
    private readonly Mock<ILabelRepository> _labelRepoMock = new();
    private readonly BoardContextBuilder _builder;

    public BoardContextBuilderTests()
    {
        _unitOfWorkMock.SetupGet(u => u.Boards).Returns(_boardRepoMock.Object);
        _unitOfWorkMock.SetupGet(u => u.Columns).Returns(_columnRepoMock.Object);
        _unitOfWorkMock.SetupGet(u => u.Cards).Returns(_cardRepoMock.Object);
        _unitOfWorkMock.SetupGet(u => u.Labels).Returns(_labelRepoMock.Object);
        _builder = new BoardContextBuilder(_unitOfWorkMock.Object);
    }

    [Fact]
    public async Task BuildContextAsync_ReturnsNull_WhenBoardDoesNotExist()
    {
        var boardId = Guid.NewGuid();
        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync((Board?)null);

        var result = await _builder.BuildContextAsync(boardId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task BuildContextAsync_IncludesBoardName()
    {
        var board = new Board("Sprint Planning", "A board for planning", Guid.NewGuid());
        var boardId = board.Id;

        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(Array.Empty<Column>());
        _labelRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(Array.Empty<Label>());

        var result = await _builder.BuildContextAsync(boardId);

        result.Should().NotBeNull();
        result.Should().Contain("Sprint Planning");
        result.Should().Contain("## Current Board Context");
    }

    [Fact]
    public async Task BuildContextAsync_IncludesColumnFlowLine()
    {
        var board = new Board("Dev Board", ownerId: Guid.NewGuid());
        var boardId = board.Id;

        var col1 = new Column(boardId, "To Do", 0);
        var col2 = new Column(boardId, "In Progress", 1);
        var col3 = new Column(boardId, "Done", 2);

        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { col1, col2, col3 });
        _cardRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(Array.Empty<Card>());
        _labelRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(Array.Empty<Label>());

        var result = await _builder.BuildContextAsync(boardId);

        result.Should().Contain("Columns: To Do → In Progress → Done");
    }

    [Fact]
    public async Task BuildContextAsync_IncludesCardIdsAndTitlesUnderColumns()
    {
        var board = new Board("Dev Board", ownerId: Guid.NewGuid());
        var boardId = board.Id;

        var col = new Column(boardId, "To Do", 0);
        var colId = col.Id;

        var card1 = new Card(boardId, colId, "Fix login bug");
        var card2 = new Card(boardId, colId, "Update README");

        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { col });
        _cardRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { card1, card2 });
        _labelRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(Array.Empty<Label>());

        var result = await _builder.BuildContextAsync(boardId);

        result.Should().Contain("Fix login bug");
        result.Should().Contain("Update README");

        // Card IDs should appear as short hex prefixes in brackets
        var shortId1 = BoardContextBuilder.FormatShortId(card1.Id);
        var shortId2 = BoardContextBuilder.FormatShortId(card2.Id);
        result.Should().Contain($"[{shortId1}]");
        result.Should().Contain($"[{shortId2}]");

        // Cards should appear under column heading
        result.Should().Contain("Cards in \"To Do\":");
    }

    [Fact]
    public async Task BuildContextAsync_IncludesLabelNames()
    {
        var board = new Board("Dev Board", ownerId: Guid.NewGuid());
        var boardId = board.Id;

        var label1 = new Label(boardId, "Bug", "#FF0000");
        var label2 = new Label(boardId, "Feature", "#00FF00");

        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(Array.Empty<Column>());
        _labelRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { label1, label2 });

        var result = await _builder.BuildContextAsync(boardId);

        result.Should().Contain("Labels: Bug, Feature");
    }

    [Fact]
    public async Task BuildContextAsync_LimitsCardsPerColumn()
    {
        var board = new Board("Dev Board", ownerId: Guid.NewGuid());
        var boardId = board.Id;

        var col = new Column(boardId, "Backlog", 0);
        var colId = col.Id;

        // Create more cards than the limit
        var cards = Enumerable.Range(1, 10)
            .Select(i => new Card(boardId, colId, $"Card {i:D2}"))
            .ToList();

        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { col });
        _cardRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(cards);
        _labelRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(Array.Empty<Label>());

        var result = await _builder.BuildContextAsync(boardId);

        // Should include at most MaxCardsPerColumn card titles
        var cardMentions = Enumerable.Range(1, 10)
            .Count(i => result!.Contains($"Card {i:D2}"));

        cardMentions.Should().BeLessThanOrEqualTo(BoardContextBuilder.MaxCardsPerColumn);
    }

    [Fact]
    public async Task BuildContextAsync_RespectsTokenBudget()
    {
        var board = new Board("Dev Board", ownerId: Guid.NewGuid());
        var boardId = board.Id;

        // Create many columns with many cards to exceed the budget
        var columns = Enumerable.Range(0, 20)
            .Select(i => new Column(boardId, $"Column {i} with a long name for testing", i))
            .ToList();

        var labels = Enumerable.Range(0, 20)
            .Select(i => new Label(boardId, $"Label-{i}", "#FF0000"))
            .ToList();

        var allCards = columns.SelectMany(col =>
            Enumerable.Range(0, 10)
                .Select(j => new Card(boardId, col.Id, $"A card with a fairly long title in column {col.Name} number {j}"))
        ).ToList();

        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(columns);
        _cardRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(allCards);
        _labelRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(labels);

        var result = await _builder.BuildContextAsync(boardId);

        // Must stay strictly within budget (truncation marker is included in the limit)
        result!.Length.Should().BeLessThanOrEqualTo(BoardContextBuilder.MaxContextCharacters);
    }

    [Fact]
    public async Task BuildContextAsync_OmitsLabelsSection_WhenNoLabels()
    {
        var board = new Board("Dev Board", ownerId: Guid.NewGuid());
        var boardId = board.Id;

        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(Array.Empty<Column>());
        _labelRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(Array.Empty<Label>());

        var result = await _builder.BuildContextAsync(boardId);

        result.Should().NotContain("Labels:");
    }

    [Fact]
    public async Task BuildContextAsync_OmitsColumnsSection_WhenNoColumns()
    {
        var board = new Board("Empty Board", ownerId: Guid.NewGuid());
        var boardId = board.Id;

        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(Array.Empty<Column>());
        _labelRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(Array.Empty<Label>());

        var result = await _builder.BuildContextAsync(boardId);

        result.Should().NotContain("Columns:");
        result.Should().NotContain("Cards in");
    }

    [Fact]
    public async Task BuildContextAsync_SkipsEmptyColumns()
    {
        var board = new Board("Dev Board", ownerId: Guid.NewGuid());
        var boardId = board.Id;

        var col1 = new Column(boardId, "Empty Column", 0);
        var col2 = new Column(boardId, "Has Cards", 1);
        var card = new Card(boardId, col2.Id, "A card");

        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { col1, col2 });
        _cardRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { card });
        _labelRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(Array.Empty<Label>());

        var result = await _builder.BuildContextAsync(boardId);

        result.Should().NotContain("Cards in \"Empty Column\"");
        result.Should().Contain("Cards in \"Has Cards\"");
    }

    [Fact]
    public void FormatShortId_ReturnsFirst8HexChars()
    {
        var id = Guid.Parse("abcdef12-3456-7890-abcd-ef1234567890");
        var shortId = BoardContextBuilder.FormatShortId(id);

        shortId.Should().Be("abcdef12");
        shortId.Should().HaveLength(BoardContextBuilder.ShortIdLength);
    }

    [Fact]
    public async Task BuildContextAsync_IncludesCardLabels()
    {
        var board = new Board("Dev Board", ownerId: Guid.NewGuid());
        var boardId = board.Id;

        var label = new Label(boardId, "Bug", "#FF0000");
        var col = new Column(boardId, "To Do", 0);
        var card = new Card(boardId, col.Id, "Fix crash");
        card.AddLabel(new CardLabel(card.Id, label.Id));

        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { col });
        _cardRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { card });
        _labelRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(new[] { label });

        var result = await _builder.BuildContextAsync(boardId);

        result.Should().Contain("Fix crash [Bug]");
    }

    [Fact]
    public async Task BuildContextAsync_BudgetIs4000()
    {
        // Verify the budget constant is 4000 chars
        BoardContextBuilder.MaxContextCharacters.Should().Be(4000);
    }
}
