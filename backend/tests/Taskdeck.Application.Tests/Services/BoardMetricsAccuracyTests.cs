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

/// <summary>
/// Accuracy verification tests for BoardMetricsService.
/// Covers done column heuristic resolution, throughput calculation accuracy,
/// cycle time computation correctness, WIP counting, blocked card detection,
/// date range/label filtering, and edge cases.
/// Tracking issue: #718 (TST-51)
/// </summary>
public class BoardMetricsAccuracyTests
{
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IBoardRepository> _boardRepoMock;
    private readonly Mock<IColumnRepository> _columnRepoMock;
    private readonly Mock<ICardRepository> _cardRepoMock;
    private readonly Mock<IAuditLogRepository> _auditLogRepoMock;
    private readonly Mock<IAuthorizationService> _authServiceMock;
    private readonly BoardMetricsService _service;

    private readonly Guid _boardId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public BoardMetricsAccuracyTests()
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

        _service = new BoardMetricsService(_unitOfWorkMock.Object, _authServiceMock.Object);
    }

    #region Done Column Detection Accuracy

    [Theory]
    [InlineData("Done")]
    [InlineData("Complete")]
    [InlineData("Completed")]
    [InlineData("Finished")]
    [InlineData("Closed")]
    [InlineData("Shipped")]
    [InlineData("Released")]
    public void ResolveDoneColumn_ShouldDetectAllKnownDoneNames(string doneColumnName)
    {
        var backlog = CreateColumn("Backlog", 0);
        var doing = CreateColumn("Doing", 1);
        var done = CreateColumn(doneColumnName, 2);

        var result = BoardMetricsService.ResolveDoneColumn(
            new List<Column> { backlog, doing, done });

        result.Should().NotBeNull();
        result!.Name.Should().Be(doneColumnName);
    }

    [Theory]
    [InlineData("DONE")]
    [InlineData("done")]
    [InlineData("Done")]
    [InlineData("dOnE")]
    [InlineData("COMPLETED")]
    [InlineData("completed")]
    [InlineData("SHIPPED")]
    public void ResolveDoneColumn_ShouldMatchCaseInsensitively(string doneColumnName)
    {
        var todo = CreateColumn("Todo", 0);
        var done = CreateColumn(doneColumnName, 1);

        var result = BoardMetricsService.ResolveDoneColumn(
            new List<Column> { todo, done });

        result.Should().NotBeNull();
        result!.Name.Should().Be(doneColumnName);
    }

    [Fact]
    public void ResolveDoneColumn_NoRecognizableNames_FallsBackToLastPosition()
    {
        // Issue scenario: columns named "Alpha", "Beta", "Gamma"
        var alpha = CreateColumn("Alpha", 0);
        var beta = CreateColumn("Beta", 1);
        var gamma = CreateColumn("Gamma", 2);

        var result = BoardMetricsService.ResolveDoneColumn(
            new List<Column> { alpha, beta, gamma });

        result.Should().NotBeNull();
        result!.Name.Should().Be("Gamma", "rightmost column should be treated as done when no name matches");
    }

    [Fact]
    public void ResolveDoneColumn_MultipleDoneLikeColumns_PicksHighestPosition()
    {
        // Both "Done" and "Completed" match — should pick the one with higher position
        var backlog = CreateColumn("Backlog", 0);
        var done = CreateColumn("Done", 1);
        var completed = CreateColumn("Completed", 2);

        var result = BoardMetricsService.ResolveDoneColumn(
            new List<Column> { backlog, done, completed });

        result.Should().NotBeNull();
        result!.Name.Should().Be("Completed",
            "when multiple done-like columns exist, the one with the highest position wins");
    }

    [Fact]
    public void ResolveDoneColumn_MultipleDoneLikeColumns_ReversedPosition()
    {
        // "Completed" at position 1, "Done" at position 2
        var backlog = CreateColumn("Backlog", 0);
        var completed = CreateColumn("Completed", 1);
        var done = CreateColumn("Done", 2);

        var result = BoardMetricsService.ResolveDoneColumn(
            new List<Column> { backlog, completed, done });

        result.Should().NotBeNull();
        result!.Name.Should().Be("Done",
            "highest position among done-like columns should win regardless of name priority");
    }

    [Fact]
    public void ResolveDoneColumn_DoneColumnNotRightmost_StillDetected()
    {
        // "Done" exists but is not the rightmost — should still be detected by name
        var backlog = CreateColumn("Backlog", 0);
        var done = CreateColumn("Done", 1);
        var archive = CreateColumn("Archive", 2);

        var result = BoardMetricsService.ResolveDoneColumn(
            new List<Column> { backlog, done, archive });

        result.Should().NotBeNull();
        result!.Name.Should().Be("Done",
            "name-based detection should take precedence over positional fallback");
    }

    [Fact]
    public void ResolveDoneColumn_SingleColumn_ReturnsThatColumn()
    {
        var only = CreateColumn("Inbox", 0);

        var result = BoardMetricsService.ResolveDoneColumn(new List<Column> { only });

        result.Should().NotBeNull();
        result!.Name.Should().Be("Inbox");
    }

    #endregion

    #region Throughput Accuracy

    [Fact]
    public void ComputeThroughput_FiveCardsMovedToDone_ReturnsThroughputOfFive()
    {
        var doneCol = CreateColumn("Done", 2);
        var baseDate = new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);

        var cards = Enumerable.Range(0, 5)
            .Select(i => CreateCard(doneCol.Id, $"Card {i}"))
            .ToList();

        var audits = new Dictionary<Guid, List<(DateTimeOffset, Guid)>>();
        foreach (var card in cards)
        {
            audits[card.Id] = new List<(DateTimeOffset, Guid)>
            {
                (baseDate.AddHours(i(cards, card)), doneCol.Id)
            };
        }

        var result = BoardMetricsService.ComputeThroughput(
            cards, doneCol, baseDate.AddDays(-1), baseDate.AddDays(1), audits);

        result.Sum(dp => dp.CompletedCount).Should().Be(5,
            "5 cards moved to done should produce a throughput of 5");
    }

    [Fact]
    public void ComputeThroughput_CardMovedToDoneTwice_CountedTwice()
    {
        // Issue scenario #10: card moved to Done, back out, then to Done again
        var doneCol = CreateColumn("Done", 2);
        var baseDate = new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);
        var card = CreateCard(doneCol.Id, "Bouncing Card");

        // Two separate moves to done column in audit trail
        var audits = new Dictionary<Guid, List<(DateTimeOffset, Guid)>>
        {
            {
                card.Id, new List<(DateTimeOffset, Guid)>
                {
                    (baseDate, doneCol.Id),
                    (baseDate.AddDays(1), doneCol.Id)
                }
            }
        };

        var result = BoardMetricsService.ComputeThroughput(
            new List<Card> { card }, doneCol, baseDate.AddDays(-1), baseDate.AddDays(2), audits);

        // The service counts each move to done as a separate completion
        result.Sum(dp => dp.CompletedCount).Should().Be(2,
            "each audit move to done column should count as a separate completion");
    }

    [Fact]
    public void ComputeThroughput_MultipleCardsOnSameDay_GroupedCorrectly()
    {
        var doneCol = CreateColumn("Done", 2);
        var day = new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero);

        var card1 = CreateCard(doneCol.Id, "Card 1");
        var card2 = CreateCard(doneCol.Id, "Card 2");
        var card3 = CreateCard(doneCol.Id, "Card 3");

        var audits = new Dictionary<Guid, List<(DateTimeOffset, Guid)>>
        {
            { card1.Id, new List<(DateTimeOffset, Guid)> { (day.AddHours(9), doneCol.Id) } },
            { card2.Id, new List<(DateTimeOffset, Guid)> { (day.AddHours(14), doneCol.Id) } },
            { card3.Id, new List<(DateTimeOffset, Guid)> { (day.AddHours(17), doneCol.Id) } },
        };

        var result = BoardMetricsService.ComputeThroughput(
            new List<Card> { card1, card2, card3 },
            doneCol, day.AddDays(-1), day.AddDays(1), audits);

        result.Should().HaveCount(1, "all completions on same day should be grouped into one data point");
        result[0].CompletedCount.Should().Be(3);
    }

    [Fact]
    public void ComputeThroughput_CardsOnDifferentDays_SeparateDataPoints()
    {
        var doneCol = CreateColumn("Done", 2);
        var day1 = new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero);
        var day2 = new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero);

        var card1 = CreateCard(doneCol.Id, "Card 1");
        var card2 = CreateCard(doneCol.Id, "Card 2");

        var audits = new Dictionary<Guid, List<(DateTimeOffset, Guid)>>
        {
            { card1.Id, new List<(DateTimeOffset, Guid)> { (day1, doneCol.Id) } },
            { card2.Id, new List<(DateTimeOffset, Guid)> { (day2, doneCol.Id) } },
        };

        var result = BoardMetricsService.ComputeThroughput(
            new List<Card> { card1, card2 },
            doneCol, day1.AddDays(-1), day2.AddDays(1), audits);

        result.Should().HaveCount(2, "completions on different days should be separate data points");
        result.Should().BeInAscendingOrder(dp => dp.Date);
    }

    [Fact]
    public void ComputeThroughput_CardMovedToNonDoneColumn_NotCounted()
    {
        var doingCol = CreateColumn("Doing", 1);
        var doneCol = CreateColumn("Done", 2);
        var baseDate = new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);
        var card = CreateCard(doingCol.Id, "In Progress Card");

        // Card moved to doing, not done
        var audits = new Dictionary<Guid, List<(DateTimeOffset, Guid)>>
        {
            { card.Id, new List<(DateTimeOffset, Guid)> { (baseDate, doingCol.Id) } }
        };

        var result = BoardMetricsService.ComputeThroughput(
            new List<Card> { card }, doneCol, baseDate.AddDays(-1), baseDate.AddDays(1), audits);

        // No audit moves to done, and card is not in done column, so fallback also empty
        result.Should().BeEmpty("moves to non-done columns should not count as throughput");
    }

    [Fact]
    public void ComputeThroughput_NoAuditData_FallsBackToCardsInDoneColumn()
    {
        var doneCol = CreateColumn("Done", 2);
        var now = DateTimeOffset.UtcNow;

        // Card currently in done column with UpdatedAt in range
        var card = CreateCard(doneCol.Id, "Already Done");

        var result = BoardMetricsService.ComputeThroughput(
            new List<Card> { card },
            doneCol,
            now.AddDays(-7),
            now.AddDays(1),
            new Dictionary<Guid, List<(DateTimeOffset, Guid)>>());

        result.Sum(dp => dp.CompletedCount).Should().Be(1,
            "with no audit data, cards in done column within date range should be counted via fallback");
    }

    #endregion

    #region Cycle Time Accuracy

    [Fact]
    public void ComputeCycleTime_CardCreatedAndMovedToDoneAfterTwoDays_CycleTimeIsTwoDays()
    {
        var doneCol = CreateColumn("Done", 2);
        var createdAt = new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.Zero);
        var movedAt = new DateTimeOffset(2026, 3, 12, 10, 0, 0, TimeSpan.Zero);

        var card = CreateCardWithCreatedAt(doneCol.Id, "Two Day Card", createdAt);

        var audits = new Dictionary<Guid, List<(DateTimeOffset, Guid)>>
        {
            { card.Id, new List<(DateTimeOffset, Guid)> { (movedAt, doneCol.Id) } }
        };

        var (avg, entries) = BoardMetricsService.ComputeCycleTime(
            new List<Card> { card }, doneCol,
            createdAt.AddDays(-1), movedAt.AddDays(1), audits);

        entries.Should().HaveCount(1);
        entries[0].CycleTimeDays.Should().Be(2.0,
            "card created on March 10, moved to done on March 12 = 2 days cycle time");
    }

    [Fact]
    public void ComputeCycleTime_CardMovedThroughMultipleColumns_CycleTimeIsCreationToDone()
    {
        var doingCol = CreateColumn("Doing", 1);
        var reviewCol = CreateColumn("Review", 2);
        var doneCol = CreateColumn("Done", 3);

        var createdAt = new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.Zero);
        var card = CreateCardWithCreatedAt(doneCol.Id, "Multi-Column Card", createdAt);

        // Card moved through doing -> review -> done, but only the done move matters for cycle time
        var audits = new Dictionary<Guid, List<(DateTimeOffset, Guid)>>
        {
            {
                card.Id, new List<(DateTimeOffset, Guid)>
                {
                    (createdAt.AddDays(1), doingCol.Id),
                    (createdAt.AddDays(2), reviewCol.Id),
                    (createdAt.AddDays(3), doneCol.Id),
                }
            }
        };

        var (avg, entries) = BoardMetricsService.ComputeCycleTime(
            new List<Card> { card }, doneCol,
            createdAt.AddDays(-1), createdAt.AddDays(5), audits);

        entries.Should().HaveCount(1);
        entries[0].CycleTimeDays.Should().Be(3.0,
            "cycle time should be from creation to the first move to done column (3 days)");
    }

    [Fact]
    public void ComputeCycleTime_MultipleCards_AverageIsCorrect()
    {
        var doneCol = CreateColumn("Done", 2);
        var baseDate = new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.Zero);

        // Card A: created on day 0, done on day 2 = 2 days
        var cardA = CreateCardWithCreatedAt(doneCol.Id, "Card A", baseDate);
        // Card B: created on day 0, done on day 4 = 4 days
        var cardB = CreateCardWithCreatedAt(doneCol.Id, "Card B", baseDate);

        var audits = new Dictionary<Guid, List<(DateTimeOffset, Guid)>>
        {
            { cardA.Id, new List<(DateTimeOffset, Guid)> { (baseDate.AddDays(2), doneCol.Id) } },
            { cardB.Id, new List<(DateTimeOffset, Guid)> { (baseDate.AddDays(4), doneCol.Id) } },
        };

        var (avg, entries) = BoardMetricsService.ComputeCycleTime(
            new List<Card> { cardA, cardB }, doneCol,
            baseDate.AddDays(-1), baseDate.AddDays(5), audits);

        entries.Should().HaveCount(2);
        avg.Should().Be(3.0, "average of 2 days and 4 days = 3 days");
    }

    [Fact]
    public void ComputeCycleTime_CardNeverReachedDone_ExcludedFromCalculation()
    {
        var doingCol = CreateColumn("Doing", 1);
        var doneCol = CreateColumn("Done", 2);
        var baseDate = new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.Zero);

        var card = CreateCardWithCreatedAt(doingCol.Id, "Still In Progress", baseDate);

        // Card moved to doing but never to done
        var audits = new Dictionary<Guid, List<(DateTimeOffset, Guid)>>
        {
            { card.Id, new List<(DateTimeOffset, Guid)> { (baseDate.AddDays(1), doingCol.Id) } }
        };

        var (avg, entries) = BoardMetricsService.ComputeCycleTime(
            new List<Card> { card }, doneCol,
            baseDate.AddDays(-1), baseDate.AddDays(5), audits);

        entries.Should().BeEmpty("cards never moved to done should be excluded from cycle time");
        avg.Should().Be(0);
    }

    [Fact]
    public void ComputeCycleTime_CardMovedToDoneMultipleTimes_UsesFirstMoveTimestamp()
    {
        var doneCol = CreateColumn("Done", 2);
        var createdAt = new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.Zero);
        var card = CreateCardWithCreatedAt(doneCol.Id, "Bounced Card", createdAt);

        // Moved to done on day 2, then again on day 5
        var audits = new Dictionary<Guid, List<(DateTimeOffset, Guid)>>
        {
            {
                card.Id, new List<(DateTimeOffset, Guid)>
                {
                    (createdAt.AddDays(2), doneCol.Id),
                    (createdAt.AddDays(5), doneCol.Id),
                }
            }
        };

        var (avg, entries) = BoardMetricsService.ComputeCycleTime(
            new List<Card> { card }, doneCol,
            createdAt.AddDays(-1), createdAt.AddDays(6), audits);

        entries.Should().HaveCount(1);
        entries[0].CycleTimeDays.Should().Be(2.0,
            "cycle time should use the first move to done (day 2), not the second (day 5)");
    }

    [Fact]
    public void ComputeCycleTime_EntriesAreSortedByCycleTime()
    {
        var doneCol = CreateColumn("Done", 2);
        var baseDate = new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.Zero);

        var slowCard = CreateCardWithCreatedAt(doneCol.Id, "Slow Card", baseDate);
        var fastCard = CreateCardWithCreatedAt(doneCol.Id, "Fast Card", baseDate);

        var audits = new Dictionary<Guid, List<(DateTimeOffset, Guid)>>
        {
            { slowCard.Id, new List<(DateTimeOffset, Guid)> { (baseDate.AddDays(5), doneCol.Id) } },
            { fastCard.Id, new List<(DateTimeOffset, Guid)> { (baseDate.AddDays(1), doneCol.Id) } },
        };

        var (_, entries) = BoardMetricsService.ComputeCycleTime(
            new List<Card> { slowCard, fastCard }, doneCol,
            baseDate.AddDays(-1), baseDate.AddDays(6), audits);

        entries.Should().HaveCount(2);
        entries.Should().BeInAscendingOrder(e => e.CycleTimeDays,
            "cycle time entries should be sorted from fastest to slowest");
    }

    [Fact]
    public void ComputeCycleTime_CardCreatedDirectlyInDoneColumn_ZeroCycleTime()
    {
        var doneCol = CreateColumn("Done", 2);
        var createdAt = new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.Zero);
        var card = CreateCardWithCreatedAt(doneCol.Id, "Born Done", createdAt);

        // Card was moved to done at the exact moment of creation (or created directly there)
        var audits = new Dictionary<Guid, List<(DateTimeOffset, Guid)>>
        {
            { card.Id, new List<(DateTimeOffset, Guid)> { (createdAt, doneCol.Id) } }
        };

        var (avg, entries) = BoardMetricsService.ComputeCycleTime(
            new List<Card> { card }, doneCol,
            createdAt.AddDays(-1), createdAt.AddDays(1), audits);

        entries.Should().HaveCount(1);
        entries[0].CycleTimeDays.Should().Be(0,
            "card created and immediately moved to done should have 0 cycle time");
        avg.Should().Be(0);
    }

    [Fact]
    public void ComputeCycleTime_CardNotInCardsList_SkippedGracefully()
    {
        var doneCol = CreateColumn("Done", 2);
        var baseDate = new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.Zero);

        // Audit references a card that is not in the cards list (e.g., filtered out by label)
        var phantomCardId = Guid.NewGuid();
        var audits = new Dictionary<Guid, List<(DateTimeOffset, Guid)>>
        {
            { phantomCardId, new List<(DateTimeOffset, Guid)> { (baseDate, doneCol.Id) } }
        };

        var (avg, entries) = BoardMetricsService.ComputeCycleTime(
            new List<Card>(), doneCol,
            baseDate.AddDays(-1), baseDate.AddDays(1), audits);

        entries.Should().BeEmpty("cards not in the card list should be silently skipped");
        avg.Should().Be(0);
    }

    #endregion

    #region WIP Accuracy

    [Fact]
    public void ComputeWip_AllCardsInDone_WipIsZeroExceptDoneColumn()
    {
        var todoCol = CreateColumn("To Do", 0);
        var doingCol = CreateColumn("Doing", 1);
        var doneCol = CreateColumn("Done", 2);

        var cards = new List<Card>
        {
            CreateCard(doneCol.Id, "Done 1"),
            CreateCard(doneCol.Id, "Done 2"),
            CreateCard(doneCol.Id, "Done 3"),
        };

        var result = BoardMetricsService.ComputeWip(
            new List<Column> { todoCol, doingCol, doneCol }, cards);

        result.Should().HaveCount(3);
        result[0].CardCount.Should().Be(0, "To Do should have 0 cards");
        result[1].CardCount.Should().Be(0, "Doing should have 0 cards");
        result[2].CardCount.Should().Be(3, "Done should have 3 cards");
    }

    [Fact]
    public void ComputeWip_ColumnsOrderedByPosition_NotByCreationOrder()
    {
        var col3 = CreateColumn("C", 2);
        var col1 = CreateColumn("A", 0);
        var col2 = CreateColumn("B", 1);

        var result = BoardMetricsService.ComputeWip(
            new List<Column> { col3, col1, col2 },
            new List<Card>());

        result[0].ColumnName.Should().Be("A");
        result[1].ColumnName.Should().Be("B");
        result[2].ColumnName.Should().Be("C");
    }

    [Fact]
    public void ComputeWip_IncludesWipLimitInSnapshot()
    {
        var col = CreateColumn("Doing", 0, wipLimit: 3);
        var cards = new List<Card>
        {
            CreateCard(col.Id, "Card 1"),
            CreateCard(col.Id, "Card 2"),
        };

        var result = BoardMetricsService.ComputeWip(
            new List<Column> { col }, cards);

        result[0].WipLimit.Should().Be(3);
        result[0].CardCount.Should().Be(2);
    }

    [Fact]
    public void ComputeWipFromCounts_MismatchedColumnIds_ReturnsZeroForMissing()
    {
        var col1 = CreateColumn("A", 0);
        var col2 = CreateColumn("B", 1);
        var col3 = CreateColumn("C", 2);

        // Only col2 has cards — col1 and col3 should default to 0
        var counts = new List<(Guid ColumnId, int CardCount)>
        {
            (col2.Id, 7),
        };

        var result = BoardMetricsService.ComputeWipFromCounts(
            new List<Column> { col1, col2, col3 }, counts);

        result[0].CardCount.Should().Be(0);
        result[1].CardCount.Should().Be(7);
        result[2].CardCount.Should().Be(0);
    }

    #endregion

    #region Blocked Card Accuracy

    [Fact]
    public void ComputeBlocked_MultipleBlockedCards_SortedByDurationDescending()
    {
        var colId = Guid.NewGuid();

        var recentlyBlocked = CreateCard(colId, "Recent");
        recentlyBlocked.Block("New blocker");

        var longBlocked = CreateCard(colId, "Long Blocked");
        longBlocked.Block("Old blocker");

        var (count, cards) = BoardMetricsService.ComputeBlocked(
            new List<Card> { recentlyBlocked, longBlocked });

        count.Should().Be(2);
        cards.Should().BeInDescendingOrder(c => c.BlockedDurationDays,
            "blocked cards should be sorted longest-blocked first");
    }

    [Fact]
    public void ComputeBlocked_BlockedCardHasReason_ReasonIncluded()
    {
        var colId = Guid.NewGuid();
        var card = CreateCard(colId, "Blocked Card");
        card.Block("Waiting for external API");

        var (_, cards) = BoardMetricsService.ComputeBlocked(new List<Card> { card });

        cards.Should().HaveCount(1);
        cards[0].BlockReason.Should().Be("Waiting for external API");
    }

    [Fact]
    public void ComputeBlocked_BlockedDuration_IsPositive()
    {
        var colId = Guid.NewGuid();
        var card = CreateCard(colId, "Blocked Card");
        card.Block("Some reason");

        var (_, cards) = BoardMetricsService.ComputeBlocked(new List<Card> { card });

        cards[0].BlockedDurationDays.Should().BeGreaterOrEqualTo(0,
            "blocked duration should always be non-negative");
    }

    [Fact]
    public void ComputeBlocked_EmptyList_ReturnsZero()
    {
        var (count, cards) = BoardMetricsService.ComputeBlocked(new List<Card>());

        count.Should().Be(0);
        cards.Should().BeEmpty();
    }

    [Fact]
    public void ComputeBlocked_UnblockedCard_NotCounted()
    {
        var colId = Guid.NewGuid();
        var card = CreateCard(colId, "Was Blocked");
        card.Block("Temp blocker");
        card.Unblock();

        var (count, cards) = BoardMetricsService.ComputeBlocked(new List<Card> { card });

        count.Should().Be(0, "unblocked cards should not appear in blocked metrics");
        cards.Should().BeEmpty();
    }

    #endregion

    #region ParseTargetColumnId Edge Cases

    [Fact]
    public void ParseTargetColumnId_MultipleTargetColumns_ParsesFirst()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var changes = $"target_column={first}; other_target_column={second}";

        var result = BoardMetricsService.ParseTargetColumnId(changes);

        result.Should().Be(first, "should parse the first target_column match");
    }

    [Fact]
    public void ParseTargetColumnId_NoEqualsSign_ReturnsNull()
    {
        var result = BoardMetricsService.ParseTargetColumnId("target_column is missing");
        result.Should().BeNull();
    }

    [Fact]
    public void ParseTargetColumnId_InvalidGuidFormat_ReturnsNull()
    {
        var result = BoardMetricsService.ParseTargetColumnId("target_column=not-a-valid-guid-at-all!!");
        result.Should().BeNull();
    }

    [Fact]
    public void ParseTargetColumnId_GuidWithoutDashes_ReturnsNull()
    {
        // The regex expects 36-char GUID with dashes
        var result = BoardMetricsService.ParseTargetColumnId(
            "target_column=12345678901234567890123456789012");
        result.Should().BeNull();
    }

    #endregion

    #region Integration Tests (Full Service with Mocks)

    [Fact]
    public async Task GetBoardMetricsAsync_EmptyBoardNoColumns_ReturnsZeroMetrics()
    {
        SetupBoard(new List<Column>(), new List<Card>());

        var query = new BoardMetricsQuery(_boardId,
            DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow);
        var result = await _service.GetBoardMetricsAsync(query, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Throughput.Should().BeEmpty();
        result.Value.AverageCycleTimeDays.Should().Be(0);
        result.Value.CycleTimeEntries.Should().BeEmpty();
        result.Value.WipSnapshots.Should().BeEmpty();
        result.Value.TotalWip.Should().Be(0);
        result.Value.BlockedCount.Should().Be(0);
        result.Value.BlockedCards.Should().BeEmpty();
    }

    [Fact]
    public async Task GetBoardMetricsAsync_BoardWithColumnsButNoCards_ReturnsEmptyWipSnapshots()
    {
        var todo = CreateColumn("To Do", 0);
        var doing = CreateColumn("Doing", 1);
        var done = CreateColumn("Done", 2);

        SetupBoard(new List<Column> { todo, doing, done }, new List<Card>());

        var query = new BoardMetricsQuery(_boardId,
            DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow);
        var result = await _service.GetBoardMetricsAsync(query, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.WipSnapshots.Should().HaveCount(3);
        result.Value.WipSnapshots.Should().AllSatisfy(w => w.CardCount.Should().Be(0));
        result.Value.TotalWip.Should().Be(0);
        result.Value.Throughput.Should().BeEmpty();
    }

    [Fact]
    public async Task GetBoardMetricsAsync_FromEqualsTo_ReturnsSuccessWithEmptyThroughput()
    {
        var done = CreateColumn("Done", 0);
        SetupBoard(new List<Column> { done }, new List<Card>());

        var now = new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);
        var query = new BoardMetricsQuery(_boardId, now, now);
        var result = await _service.GetBoardMetricsAsync(query, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.From.Should().Be(now);
        result.Value.To.Should().Be(now);
    }

    [Fact]
    public async Task GetBoardMetricsAsync_WideYearRange_ReturnsSuccess()
    {
        var done = CreateColumn("Done", 0);
        SetupBoard(new List<Column> { done }, new List<Card>());

        var from = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var query = new BoardMetricsQuery(_boardId, from, to);
        var result = await _service.GetBoardMetricsAsync(query, _userId);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GetBoardMetricsAsync_ResponseContainsCorrectBoardIdAndDateRange()
    {
        var done = CreateColumn("Done", 0);
        SetupBoard(new List<Column> { done }, new List<Card>());

        var from = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 3, 31, 0, 0, 0, TimeSpan.Zero);
        var query = new BoardMetricsQuery(_boardId, from, to);
        var result = await _service.GetBoardMetricsAsync(query, _userId);

        result.IsSuccess.Should().BeTrue();
        result.Value.BoardId.Should().Be(_boardId);
        result.Value.From.Should().Be(from);
        result.Value.To.Should().Be(to);
    }

    #endregion

    #region Helpers

    private static int i(List<Card> cards, Card card) => cards.IndexOf(card);

    private void SetupBoard(List<Column> columns, List<Card> cards)
    {
        var board = new Board("Test Board", ownerId: _userId);
        _boardRepoMock.Setup(r => r.GetByIdAsync(_boardId, default)).ReturnsAsync(board);
        _columnRepoMock.Setup(r => r.GetByBoardIdAsync(_boardId, default)).ReturnsAsync(columns);

        _cardRepoMock.Setup(r => r.GetByBoardIdAsync(_boardId, default)).ReturnsAsync(cards);

        var columnCounts = columns
            .Select(col => (col.Id, cards.Count(c => c.ColumnId == col.Id)))
            .ToList() as IReadOnlyList<(Guid ColumnId, int CardCount)>;
        _cardRepoMock
            .Setup(r => r.CountCardsByColumnAsync(_boardId, It.IsAny<Guid?>(), default))
            .ReturnsAsync(columnCounts);

        var blockedCards = cards.Where(c => c.IsBlocked).ToList() as IEnumerable<Card>;
        _cardRepoMock
            .Setup(r => r.GetBlockedByBoardIdAsync(_boardId, It.IsAny<Guid?>(), default))
            .ReturnsAsync(blockedCards);

        _cardRepoMock
            .Setup(r => r.GetForMetricsAsync(
                _boardId, It.IsAny<Guid?>(), It.IsAny<IEnumerable<Guid>?>(), default))
            .ReturnsAsync((Guid boardId, Guid? labelId, IEnumerable<Guid>? cardIds, CancellationToken _) =>
            {
                IEnumerable<Card> result = cards;
                if (cardIds != null)
                {
                    var ids = cardIds.ToHashSet();
                    if (ids.Count > 0)
                        result = result.Where(c => ids.Contains(c.Id));
                    else
                        return Enumerable.Empty<Card>();
                }
                return result;
            });
    }

    private Column CreateColumn(string name, int position, int? wipLimit = null)
    {
        return new Column(_boardId, name, position, wipLimit);
    }

    private Card CreateCard(Guid columnId, string title)
    {
        return new Card(_boardId, columnId, title);
    }

    /// <summary>
    /// Creates a card and uses reflection to set CreatedAt to a controlled value
    /// for deterministic cycle time calculations.
    /// </summary>
    private Card CreateCardWithCreatedAt(Guid columnId, string title, DateTimeOffset createdAt)
    {
        var card = new Card(_boardId, columnId, title);

        // Entity.CreatedAt is set in the base constructor. We need reflection
        // to override it for controlled test scenarios.
        var createdAtProp = typeof(Entity).GetProperty("CreatedAt");
        if (createdAtProp != null && createdAtProp.CanWrite)
        {
            createdAtProp.SetValue(card, createdAt);
        }
        else
        {
            // If no public setter, use the backing field
            var field = typeof(Entity).GetField("_createdAt",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(card, createdAt);
            }
            else
            {
                // Try property backing field convention
                var backingField = typeof(Entity).GetField("<CreatedAt>k__BackingField",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                backingField?.SetValue(card, createdAt);
            }
        }

        return card;
    }

    #endregion
}
