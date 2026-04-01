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

public class BoardMetricsServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IBoardRepository> _boardRepoMock;
    private readonly Mock<IColumnRepository> _columnRepoMock;
    private readonly Mock<ICardRepository> _cardRepoMock;
    private readonly Mock<IAuthorizationService> _authServiceMock;
    private readonly BoardMetricsService _service;

    private readonly Guid _boardId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public BoardMetricsServiceTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _boardRepoMock = new Mock<IBoardRepository>();
        _columnRepoMock = new Mock<IColumnRepository>();
        _cardRepoMock = new Mock<ICardRepository>();
        _authServiceMock = new Mock<IAuthorizationService>();

        _unitOfWorkMock.Setup(u => u.Boards).Returns(_boardRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Columns).Returns(_columnRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Cards).Returns(_cardRepoMock.Object);

        _authServiceMock
            .Setup(a => a.CanReadBoardAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
            .ReturnsAsync(Result.Success(true));

        _service = new BoardMetricsService(_unitOfWorkMock.Object, _authServiceMock.Object);
    }

    #region Validation Tests

    [Fact]
    public async Task GetBoardMetricsAsync_ShouldFail_WhenBoardIdIsEmpty()
    {
        var query = new BoardMetricsQuery(Guid.Empty, DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow);

        var result = await _service.GetBoardMetricsAsync(query, _userId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task GetBoardMetricsAsync_ShouldFail_WhenUserIdIsEmpty()
    {
        var query = new BoardMetricsQuery(_boardId, DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow);

        var result = await _service.GetBoardMetricsAsync(query, Guid.Empty);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task GetBoardMetricsAsync_ShouldFail_WhenFromIsAfterTo()
    {
        var now = DateTimeOffset.UtcNow;
        var query = new BoardMetricsQuery(_boardId, now, now.AddDays(-7));

        var result = await _service.GetBoardMetricsAsync(query, _userId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        result.ErrorMessage.Should().Contain("before");
    }

    [Fact]
    public async Task GetBoardMetricsAsync_ShouldFail_WhenBoardNotFound()
    {
        _boardRepoMock.Setup(r => r.GetByIdAsync(_boardId, default)).ReturnsAsync((Board?)null);

        var query = new BoardMetricsQuery(_boardId, DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow);
        var result = await _service.GetBoardMetricsAsync(query, _userId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task GetBoardMetricsAsync_ShouldFail_WhenUserLacksPermission()
    {
        _authServiceMock
            .Setup(a => a.CanReadBoardAsync(_userId, _boardId))
            .ReturnsAsync(Result.Success(false));

        var query = new BoardMetricsQuery(_boardId, DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow);
        var result = await _service.GetBoardMetricsAsync(query, _userId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    #endregion

    #region Successful Metrics Computation

    [Fact]
    public async Task GetBoardMetricsAsync_ShouldReturnMetrics_WithEmptyBoard()
    {
        SetupBoard(new List<Column>(), new List<Card>());

        var query = new BoardMetricsQuery(_boardId, DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow);
        var result = await _service.GetBoardMetricsAsync(query, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Throughput.Should().BeEmpty();
        result.Value.AverageCycleTimeDays.Should().Be(0);
        result.Value.WipSnapshots.Should().BeEmpty();
        result.Value.BlockedCount.Should().Be(0);
        result.Value.TotalWip.Should().Be(0);
    }

    [Fact]
    public async Task GetBoardMetricsAsync_ShouldComputeWip_ForEachColumn()
    {
        var todoCol = CreateColumn("To Do", 0);
        var doingCol = CreateColumn("Doing", 1);
        var doneCol = CreateColumn("Done", 2);

        var cards = new List<Card>
        {
            CreateCard(todoCol.Id, "Card 1"),
            CreateCard(todoCol.Id, "Card 2"),
            CreateCard(doingCol.Id, "Card 3"),
            CreateCard(doneCol.Id, "Card 4"),
        };

        SetupBoard(new List<Column> { todoCol, doingCol, doneCol }, cards);

        var query = new BoardMetricsQuery(_boardId, DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow);
        var result = await _service.GetBoardMetricsAsync(query, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.WipSnapshots.Should().HaveCount(3);
        result.Value.WipSnapshots[0].ColumnName.Should().Be("To Do");
        result.Value.WipSnapshots[0].CardCount.Should().Be(2);
        result.Value.WipSnapshots[1].ColumnName.Should().Be("Doing");
        result.Value.WipSnapshots[1].CardCount.Should().Be(1);
        result.Value.WipSnapshots[2].ColumnName.Should().Be("Done");
        result.Value.WipSnapshots[2].CardCount.Should().Be(1);
        result.Value.TotalWip.Should().Be(4);
    }

    [Fact]
    public async Task GetBoardMetricsAsync_ShouldCountBlockedCards()
    {
        var todoCol = CreateColumn("To Do", 0);
        var doneCol = CreateColumn("Done", 1);

        var card1 = CreateCard(todoCol.Id, "Blocked Card");
        card1.Block("Waiting on dependency");
        var card2 = CreateCard(todoCol.Id, "Active Card");

        SetupBoard(new List<Column> { todoCol, doneCol }, new List<Card> { card1, card2 });

        var query = new BoardMetricsQuery(_boardId, DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow);
        var result = await _service.GetBoardMetricsAsync(query, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.BlockedCount.Should().Be(1);
        result.Value.BlockedCards.Should().HaveCount(1);
        result.Value.BlockedCards[0].CardTitle.Should().Be("Blocked Card");
        result.Value.BlockedCards[0].BlockReason.Should().Be("Waiting on dependency");
    }

    #endregion

    #region Static Computation Tests

    [Fact]
    public void ComputeThroughput_ShouldReturnEmpty_WhenNoDoneColumn()
    {
        var result = BoardMetricsService.ComputeThroughput(
            new List<Card>(),
            null,
            DateTimeOffset.UtcNow.AddDays(-7),
            DateTimeOffset.UtcNow);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ComputeCycleTime_ShouldReturnZero_WhenNoDoneColumn()
    {
        var (avg, entries) = BoardMetricsService.ComputeCycleTime(
            new List<Card>(),
            null,
            DateTimeOffset.UtcNow.AddDays(-7),
            DateTimeOffset.UtcNow);

        avg.Should().Be(0);
        entries.Should().BeEmpty();
    }

    [Fact]
    public void ComputeWip_ShouldCountCardsPerColumn()
    {
        var col1 = CreateColumn("A", 0);
        var col2 = CreateColumn("B", 1);
        var cards = new List<Card>
        {
            CreateCard(col1.Id, "Card 1"),
            CreateCard(col1.Id, "Card 2"),
            CreateCard(col2.Id, "Card 3"),
        };

        var result = BoardMetricsService.ComputeWip(
            new List<Column> { col1, col2 },
            cards);

        result.Should().HaveCount(2);
        result[0].CardCount.Should().Be(2);
        result[1].CardCount.Should().Be(1);
    }

    [Fact]
    public void ComputeBlocked_ShouldReturnOnlyBlockedCards()
    {
        var colId = Guid.NewGuid();
        var blocked = CreateCard(colId, "Blocked");
        blocked.Block("Some reason");
        var active = CreateCard(colId, "Active");

        var (count, cards) = BoardMetricsService.ComputeBlocked(
            new List<Card> { blocked, active });

        count.Should().Be(1);
        cards.Should().HaveCount(1);
        cards[0].CardTitle.Should().Be("Blocked");
    }

    #endregion

    #region Helpers

    private void SetupBoard(List<Column> columns, List<Card> cards)
    {
        var board = new Board("Test Board", ownerId: _userId);
        _boardRepoMock.Setup(r => r.GetByIdAsync(_boardId, default)).ReturnsAsync(board);
        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(_boardId, default)).ReturnsAsync(columns);
        _cardRepoMock.Setup(r => r.GetByBoardIdAsync(_boardId, default)).ReturnsAsync(cards);
    }

    private Column CreateColumn(string name, int position, int? wipLimit = null)
    {
        return new Column(_boardId, name, position, wipLimit);
    }

    private Card CreateCard(Guid columnId, string title)
    {
        return new Card(_boardId, columnId, title);
    }

    #endregion
}
