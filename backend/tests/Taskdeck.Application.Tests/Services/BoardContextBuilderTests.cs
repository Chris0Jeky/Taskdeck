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
        _cardRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(Array.Empty<Card>());
        _labelRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(Array.Empty<Label>());

        var result = await _builder.BuildContextAsync(boardId);

        result.Should().NotBeNull();
        result.Should().Contain("Sprint Planning");
        result.Should().Contain("## Current Board Context");
    }

    [Fact]
    public async Task BuildContextAsync_IncludesColumnNamesAndPositions()
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

        result.Should().Contain("To Do");
        result.Should().Contain("In Progress");
        result.Should().Contain("Done");
        result.Should().Contain("position 0");
        result.Should().Contain("position 1");
        result.Should().Contain("position 2");
    }

    [Fact]
    public async Task BuildContextAsync_IncludesCardTitlesUnderColumns()
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
        _cardRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(Array.Empty<Card>());
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

        var cards = new List<Card>();
        foreach (var col in columns)
        {
            for (int j = 0; j < 10; j++)
            {
                cards.Add(new Card(boardId, col.Id, $"A card with a fairly long title in column {col.Name} number {j}"));
            }
        }

        var labels = Enumerable.Range(0, 20)
            .Select(i => new Label(boardId, $"Label-{i}", "#FF0000"))
            .ToList();

        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(columns);
        _cardRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(cards);
        _labelRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(labels);

        var result = await _builder.BuildContextAsync(boardId);

        // Must stay within budget (with small overflow for truncation marker)
        result!.Length.Should().BeLessThanOrEqualTo(BoardContextBuilder.MaxContextCharacters + 20);
    }

    [Fact]
    public async Task BuildContextAsync_OmitsLabelsSection_WhenNoLabels()
    {
        var board = new Board("Dev Board", ownerId: Guid.NewGuid());
        var boardId = board.Id;

        _boardRepoMock.Setup(r => r.GetByIdAsync(boardId, default)).ReturnsAsync(board);
        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(Array.Empty<Column>());
        _cardRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(Array.Empty<Card>());
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
        _cardRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(Array.Empty<Card>());
        _labelRepoMock.Setup(r => r.GetByBoardIdAsync(boardId, default)).ReturnsAsync(Array.Empty<Label>());

        var result = await _builder.BuildContextAsync(boardId);

        result.Should().NotContain("Columns (in order):");
    }
}
