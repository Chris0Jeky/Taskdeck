using FluentAssertions;
using Moq;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;
using Xunit;

namespace Taskdeck.Application.Tests.Services;

public class ForecastingServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IBoardRepository> _boardRepoMock;
    private readonly Mock<IColumnRepository> _columnRepoMock;
    private readonly Mock<ICardRepository> _cardRepoMock;
    private readonly Mock<IAuditLogRepository> _auditLogRepoMock;
    private readonly Mock<IAuthorizationService> _authServiceMock;
    private readonly ForecastingService _service;

    private readonly Guid _boardId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public ForecastingServiceTests()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _boardRepoMock = new Mock<IBoardRepository>();
        _columnRepoMock = new Mock<IColumnRepository>();
        _cardRepoMock = new Mock<ICardRepository>();
        _auditLogRepoMock = new Mock<IAuditLogRepository>();
        _authServiceMock = new Mock<IAuthorizationService>();

        _unitOfWorkMock.Setup(u => u.Boards).Returns(_boardRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Columns).Returns(_columnRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.Cards).Returns(_cardRepoMock.Object);
        _unitOfWorkMock.Setup(u => u.AuditLogs).Returns(_auditLogRepoMock.Object);

        _authServiceMock
            .Setup(a => a.CanReadBoardAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
            .ReturnsAsync(Result.Success(true));

        _auditLogRepoMock
            .Setup(a => a.QueryAsync(
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AuditLog>());

        _cardRepoMock
            .Setup(c => c.CountCardsByColumnAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(Guid, int)>());

        _cardRepoMock
            .Setup(c => c.GetForMetricsAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(),
                It.IsAny<IEnumerable<Guid>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<Card>());

        _service = new ForecastingService(_unitOfWorkMock.Object, _authServiceMock.Object);
    }

    #region Validation Tests

    [Fact]
    public async Task GetBoardForecastAsync_ShouldFail_WhenBoardIdIsEmpty()
    {
        var query = new BoardForecastQuery(Guid.Empty);
        var result = await _service.GetBoardForecastAsync(query, _userId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task GetBoardForecastAsync_ShouldFail_WhenUserIdIsEmpty()
    {
        var query = new BoardForecastQuery(_boardId);
        var result = await _service.GetBoardForecastAsync(query, Guid.Empty);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(366)]
    [InlineData(500)]
    public async Task GetBoardForecastAsync_ShouldFail_WhenHistoryDaysOutOfRange(int historyDays)
    {
        var query = new BoardForecastQuery(_boardId, historyDays);
        var result = await _service.GetBoardForecastAsync(query, _userId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task GetBoardForecastAsync_ShouldFail_WhenUserLacksPermission()
    {
        _authServiceMock
            .Setup(a => a.CanReadBoardAsync(_userId, _boardId))
            .ReturnsAsync(Result.Success(false));

        var query = new BoardForecastQuery(_boardId);
        var result = await _service.GetBoardForecastAsync(query, _userId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task GetBoardForecastAsync_ShouldFail_WhenBoardNotFound()
    {
        _boardRepoMock.Setup(b => b.GetByIdAsync(_boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Board?)null);

        var query = new BoardForecastQuery(_boardId);
        var result = await _service.GetBoardForecastAsync(query, _userId);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    #endregion

    #region Service-Level Edge Cases

    [Fact]
    public async Task GetBoardForecastAsync_ShouldHandle_NoColumns()
    {
        var board = new Board("Test Board", ownerId: _userId);
        _boardRepoMock.Setup(b => b.GetByIdAsync(_boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(board);
        _columnRepoMock.Setup(c => c.GetByBoardIdAsync(_boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Column>());

        var query = new BoardForecastQuery(_boardId);
        var result = await _service.GetBoardForecastAsync(query, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Caveats.Should().Contain(c => c.Contains("no columns"));
        result.Value.RemainingCards.Should().Be(0);
        result.Value.CompletedCards.Should().Be(0);
    }

    [Fact]
    public async Task GetBoardForecastAsync_ShouldHandle_ZeroThroughput()
    {
        var board = new Board("Test Board", ownerId: _userId);
        var todoCol = new Column(_boardId, "To Do", 0);
        var doneCol = new Column(_boardId, "Done", 1);

        SetupBoard(board, new[] { todoCol, doneCol });
        SetupColumnCounts(new[] { (todoCol.Id, 5), (doneCol.Id, 0) });

        var query = new BoardForecastQuery(_boardId);
        var result = await _service.GetBoardForecastAsync(query, _userId);

        result.IsSuccess.Should().BeTrue();
        var forecast = result.Value;
        forecast.AverageThroughputPerDay.Should().Be(0);
        forecast.EstimatedCompletionDate.Should().BeNull();
        forecast.ConfidenceBand.Should().BeNull();
        forecast.Caveats.Should().Contain(c => c.Contains("Zero throughput"));
    }

    [Fact]
    public async Task GetBoardForecastAsync_ShouldHandle_NoRemainingCards()
    {
        var board = new Board("Test Board", ownerId: _userId);
        var todoCol = new Column(_boardId, "To Do", 0);
        var doneCol = new Column(_boardId, "Done", 1);

        SetupBoard(board, new[] { todoCol, doneCol });
        SetupColumnCounts(new[] { (todoCol.Id, 0), (doneCol.Id, 10) });

        var query = new BoardForecastQuery(_boardId);
        var result = await _service.GetBoardForecastAsync(query, _userId);

        result.IsSuccess.Should().BeTrue();
        var forecast = result.Value;
        forecast.RemainingCards.Should().Be(0);
        forecast.CompletedCards.Should().Be(10);
        forecast.Caveats.Should().Contain(c => c.Contains("complete"));
    }

    [Fact]
    public async Task GetBoardForecastAsync_ShouldUseSpecifiedHistoryDays()
    {
        var board = new Board("Test Board", ownerId: _userId);
        var todoCol = new Column(_boardId, "To Do", 0);
        var doneCol = new Column(_boardId, "Done", 1);

        SetupBoard(board, new[] { todoCol, doneCol });
        SetupColumnCounts(new[] { (todoCol.Id, 5), (doneCol.Id, 10) });

        var query = new BoardForecastQuery(_boardId, HistoryDays: 14);
        var result = await _service.GetBoardForecastAsync(query, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.HistoryDaysUsed.Should().Be(14);
    }

    [Fact]
    public async Task GetBoardForecastAsync_ShouldNoteRightmostColumnAsDone()
    {
        var board = new Board("Test Board", ownerId: _userId);
        var col1 = new Column(_boardId, "Backlog", 0);
        var col2 = new Column(_boardId, "In Progress", 1);
        var col3 = new Column(_boardId, "Review", 2);

        SetupBoard(board, new[] { col1, col2, col3 });
        SetupColumnCounts(new[] { (col1.Id, 3), (col2.Id, 2), (col3.Id, 1) });

        var query = new BoardForecastQuery(_boardId);
        var result = await _service.GetBoardForecastAsync(query, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Assumptions.Should().Contain(a => a.Contains("rightmost"));
    }

    [Fact]
    public async Task GetBoardForecastAsync_ShouldReturnAssumptions()
    {
        var board = new Board("Test Board", ownerId: _userId);
        var todoCol = new Column(_boardId, "To Do", 0);
        var doneCol = new Column(_boardId, "Done", 1);

        SetupBoard(board, new[] { todoCol, doneCol });
        SetupColumnCounts(new[] { (todoCol.Id, 5), (doneCol.Id, 0) });

        var query = new BoardForecastQuery(_boardId);
        var result = await _service.GetBoardForecastAsync(query, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Assumptions.Should().NotBeEmpty();
        result.Value.Assumptions.Should().Contain(a => a.Contains("rolling history"));
        result.Value.Assumptions.Should().Contain(a => a.Contains("Throughput"));
    }

    #endregion

    #region Static Method Tests — ResolveDoneColumn

    [Fact]
    public void ResolveDoneColumn_ShouldPreferNamedDoneColumn()
    {
        var columns = new List<Column>
        {
            new(_boardId, "To Do", 0),
            new(_boardId, "Done", 1),
            new(_boardId, "Archive", 2)
        };

        var result = ForecastingService.ResolveDoneColumn(columns);
        result.Should().NotBeNull();
        result!.Name.Should().Be("Done");
    }

    [Fact]
    public void ResolveDoneColumn_ShouldMatchCaseInsensitively()
    {
        var columns = new List<Column>
        {
            new(_boardId, "To Do", 0),
            new(_boardId, "COMPLETED", 1)
        };

        var result = ForecastingService.ResolveDoneColumn(columns);
        result.Should().NotBeNull();
        result!.Name.Should().Be("COMPLETED");
    }

    [Fact]
    public void ResolveDoneColumn_ShouldFallBackToRightmost()
    {
        var columns = new List<Column>
        {
            new(_boardId, "Backlog", 0),
            new(_boardId, "Doing", 1),
            new(_boardId, "Review", 2)
        };

        var result = ForecastingService.ResolveDoneColumn(columns);
        result.Should().NotBeNull();
        result!.Name.Should().Be("Review");
    }

    [Fact]
    public void ResolveDoneColumn_ShouldReturnNull_ForEmptyList()
    {
        var result = ForecastingService.ResolveDoneColumn(new List<Column>());
        result.Should().BeNull();
    }

    #endregion

    #region Static Method Tests — ComputeDailyThroughput

    [Fact]
    public void ComputeDailyThroughput_ShouldCountCompletionsPerDay()
    {
        var doneColId = Guid.NewGuid();
        var otherColId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var audits = new Dictionary<Guid, List<(DateTimeOffset, Guid)>>
        {
            [Guid.NewGuid()] = new() { (now.AddDays(-1), doneColId) },
            [Guid.NewGuid()] = new() { (now.AddDays(-1).AddHours(2), doneColId) },
            [Guid.NewGuid()] = new() { (now, otherColId) },
            [Guid.NewGuid()] = new() { (now, doneColId) }
        };

        var result = ForecastingService.ComputeDailyThroughput(audits, doneColId, now.AddDays(-7), now);

        result.Should().HaveCount(2);
        result.Sum(d => d.Count).Should().Be(3);
    }

    [Fact]
    public void ComputeDailyThroughput_ShouldReturnEmpty_WhenNoMovesToDone()
    {
        var doneColId = Guid.NewGuid();
        var otherColId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var audits = new Dictionary<Guid, List<(DateTimeOffset, Guid)>>
        {
            [Guid.NewGuid()] = new() { (now, otherColId) }
        };

        var result = ForecastingService.ComputeDailyThroughput(audits, doneColId, now.AddDays(-7), now);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ComputeDailyThroughput_ShouldHandleEmptyAudits()
    {
        var result = ForecastingService.ComputeDailyThroughput(
            new Dictionary<Guid, List<(DateTimeOffset, Guid)>>(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddDays(-7),
            DateTimeOffset.UtcNow);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ComputeDailyThroughput_ShouldNotDoubleCount_WhenCardBouncesToDone()
    {
        var doneColId = Guid.NewGuid();
        var inProgressColId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var cardId = Guid.NewGuid();

        // Card moves: Done → In Progress → Done (should count as 1, not 2)
        var audits = new Dictionary<Guid, List<(DateTimeOffset, Guid)>>
        {
            [cardId] = new()
            {
                (now.AddDays(-3), doneColId),
                (now.AddDays(-2), inProgressColId),
                (now.AddDays(-1), doneColId)
            }
        };

        var result = ForecastingService.ComputeDailyThroughput(audits, doneColId, now.AddDays(-7), now);

        // Should only count 1 completion (the last move to done), not 2
        result.Sum(d => d.Count).Should().Be(1);
    }

    #endregion

    #region Static Method Tests — ComputeThroughputStatistics

    [Fact]
    public void ComputeThroughputStatistics_ShouldComputeMeanAndStdDev()
    {
        var today = DateTime.UtcNow.Date;
        var points = new List<ForecastingService.DailyThroughputPoint>
        {
            new(today.AddDays(-4), 2),
            new(today.AddDays(-3), 4),
            new(today.AddDays(-2), 2),
            new(today.AddDays(-1), 4),
            new(today, 2)
        };

        // 5-day span matches history window
        var (mean, stdDev) = ForecastingService.ComputeThroughputStatistics(points, 5);

        // 14 completions over 5 days = 2.8/day
        mean.Should().BeApproximately(2.8, 0.01);
        stdDev.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ComputeThroughputStatistics_ShouldReturnZero_ForEmptyList()
    {
        var (mean, stdDev) = ForecastingService.ComputeThroughputStatistics(
            new List<ForecastingService.DailyThroughputPoint>(), 30);

        mean.Should().Be(0);
        stdDev.Should().Be(0);
    }

    [Fact]
    public void ComputeThroughputStatistics_ShouldHandle_SinglePoint()
    {
        var points = new List<ForecastingService.DailyThroughputPoint>
        {
            new(DateTime.UtcNow.Date, 5)
        };

        // Single point in a 1-day window
        var (mean, stdDev) = ForecastingService.ComputeThroughputStatistics(points, 1);

        mean.Should().Be(5);
        stdDev.Should().Be(0);
    }

    [Fact]
    public void ComputeThroughputStatistics_ShouldIncludeZeroDays_InAverage()
    {
        var today = DateTime.UtcNow.Date;
        // 2 days with completions in a 30-day window
        var points = new List<ForecastingService.DailyThroughputPoint>
        {
            new(today.AddDays(-2), 3),
            new(today, 3)
        };

        var (mean, stdDev) = ForecastingService.ComputeThroughputStatistics(points, 30);

        // 6 completions over 30-day window = 0.2/day
        mean.Should().BeApproximately(0.2, 0.01);
        stdDev.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ComputeThroughputStatistics_ShouldHandle_UniformThroughput()
    {
        var today = DateTime.UtcNow.Date;
        var points = new List<ForecastingService.DailyThroughputPoint>
        {
            new(today.AddDays(-2), 3),
            new(today.AddDays(-1), 3),
            new(today, 3)
        };

        // 3-day window matches data span
        var (mean, stdDev) = ForecastingService.ComputeThroughputStatistics(points, 3);

        mean.Should().BeApproximately(3.0, 0.01);
        stdDev.Should().BeApproximately(0, 0.01);
    }

    [Fact]
    public void ComputeThroughputStatistics_ShouldProduceNonNegativeValues()
    {
        var today = DateTime.UtcNow.Date;

        // Sparse points in a 30-day window
        var sparsePoints = new List<ForecastingService.DailyThroughputPoint>
        {
            new(today.AddDays(-4), 100),
            new(today, 1)
        };

        var (mean, stdDev) = ForecastingService.ComputeThroughputStatistics(sparsePoints, 30);

        mean.Should().BeGreaterThan(0);
        stdDev.Should().BeGreaterOrEqualTo(0);
        double.IsNaN(mean).Should().BeFalse();
        double.IsNaN(stdDev).Should().BeFalse();
        double.IsInfinity(mean).Should().BeFalse();
        double.IsInfinity(stdDev).Should().BeFalse();
    }

    [Fact]
    public void ComputeThroughputStatistics_ShouldUsHistoryWindow_NotDataSpan()
    {
        var today = DateTime.UtcNow.Date;
        // Bursty pattern: all completions in last 3 days of a 30-day window
        var points = new List<ForecastingService.DailyThroughputPoint>
        {
            new(today.AddDays(-2), 10),
            new(today.AddDays(-1), 10),
            new(today, 10)
        };

        // With full 30-day window, mean should be 30/30 = 1.0/day
        var (mean30, _) = ForecastingService.ComputeThroughputStatistics(points, 30);
        mean30.Should().BeApproximately(1.0, 0.01);

        // With 3-day window, mean should be 30/3 = 10.0/day
        var (mean3, _) = ForecastingService.ComputeThroughputStatistics(points, 3);
        mean3.Should().BeApproximately(10.0, 0.01);
    }

    #endregion

    #region Static Method Tests — ParseTargetColumnId

    [Fact]
    public void ParseTargetColumnId_ShouldParseValidChanges()
    {
        var columnId = Guid.NewGuid();
        var changes = $"target_column={columnId}; position=3";

        var result = ForecastingService.ParseTargetColumnId(changes);
        result.Should().Be(columnId);
    }

    [Fact]
    public void ParseTargetColumnId_ShouldReturnNull_ForInvalidChanges()
    {
        var result = ForecastingService.ParseTargetColumnId("some random text");
        result.Should().BeNull();
    }

    [Fact]
    public void ParseTargetColumnId_ShouldReturnNull_ForEmptyString()
    {
        var result = ForecastingService.ParseTargetColumnId("");
        result.Should().BeNull();
    }

    #endregion

    #region Helpers

    private void SetupBoard(Board board, Column[] columns)
    {
        _boardRepoMock.Setup(b => b.GetByIdAsync(_boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(board);
        _columnRepoMock.Setup(c => c.GetByBoardIdAsync(_boardId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(columns.ToList());
    }

    private void SetupColumnCounts((Guid ColumnId, int Count)[] counts)
    {
        _cardRepoMock.Setup(c => c.CountCardsByColumnAsync(
                _boardId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(counts.Select(x => (x.ColumnId, x.Count)).ToList());
    }

    #endregion
}
