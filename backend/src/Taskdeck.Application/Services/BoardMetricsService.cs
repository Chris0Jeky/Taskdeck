using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class BoardMetricsService : IBoardMetricsService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuthorizationService? _authorizationService;

    public BoardMetricsService(IUnitOfWork unitOfWork)
        : this(unitOfWork, authorizationService: null)
    {
    }

    public BoardMetricsService(
        IUnitOfWork unitOfWork,
        IAuthorizationService? authorizationService)
    {
        _unitOfWork = unitOfWork;
        _authorizationService = authorizationService;
    }

    public async Task<Result<BoardMetricsResponse>> GetBoardMetricsAsync(
        BoardMetricsQuery query,
        Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        if (query.BoardId == Guid.Empty)
            return Result.Failure<BoardMetricsResponse>(ErrorCodes.ValidationError, "Board ID is required");

        if (actingUserId == Guid.Empty)
            return Result.Failure<BoardMetricsResponse>(ErrorCodes.ValidationError, "Acting user ID cannot be empty");

        if (query.From >= query.To)
            return Result.Failure<BoardMetricsResponse>(ErrorCodes.ValidationError, "From date must be before To date");

        // Enforce read permission
        if (_authorizationService != null)
        {
            var canRead = await _authorizationService.CanReadBoardAsync(actingUserId, query.BoardId);
            if (!canRead.IsSuccess)
                return Result.Failure<BoardMetricsResponse>(canRead.ErrorCode, canRead.ErrorMessage);

            if (!canRead.Value)
                return Result.Failure<BoardMetricsResponse>(ErrorCodes.Forbidden, "You do not have permission to view metrics for this board");
        }

        // Verify board exists
        var board = await _unitOfWork.Boards.GetByIdAsync(query.BoardId, cancellationToken);
        if (board == null)
            return Result.Failure<BoardMetricsResponse>(ErrorCodes.NotFound, "Board not found");

        // Load columns and cards for the board
        var columns = (await _unitOfWork.Columns.GetByBoardIdAsync(query.BoardId, cancellationToken)).ToList();
        var cards = (await _unitOfWork.Cards.GetByBoardIdAsync(query.BoardId, cancellationToken)).ToList();

        // Filter by label if requested
        if (query.LabelId.HasValue)
        {
            cards = cards.Where(c => c.CardLabels.Any(cl => cl.LabelId == query.LabelId.Value)).ToList();
        }

        // Determine the "done" column (rightmost by position)
        var doneColumn = columns.OrderByDescending(c => c.Position).FirstOrDefault();

        var throughput = ComputeThroughput(cards, doneColumn, query.From, query.To);
        var (avgCycleTime, cycleTimeEntries) = ComputeCycleTime(cards, doneColumn, query.From, query.To);
        var wipSnapshots = ComputeWip(columns, cards);
        var totalWip = wipSnapshots.Sum(w => w.CardCount);
        var (blockedCount, blockedCards) = ComputeBlocked(cards);

        return Result.Success(new BoardMetricsResponse(
            query.BoardId,
            query.From,
            query.To,
            throughput,
            avgCycleTime,
            cycleTimeEntries,
            wipSnapshots,
            totalWip,
            blockedCount,
            blockedCards));
    }

    internal static IReadOnlyList<ThroughputDataPoint> ComputeThroughput(
        List<Card> cards,
        Column? doneColumn,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        if (doneColumn == null)
            return Array.Empty<ThroughputDataPoint>();

        // Cards in the done column that were updated (moved there) within the range
        var completedCards = cards
            .Where(c => c.ColumnId == doneColumn.Id
                        && c.UpdatedAt >= from
                        && c.UpdatedAt <= to)
            .ToList();

        // Group by date (day granularity)
        var grouped = completedCards
            .GroupBy(c => c.UpdatedAt.Date)
            .Select(g => new ThroughputDataPoint(
                new DateTimeOffset(g.Key, TimeSpan.Zero),
                g.Count()))
            .OrderBy(dp => dp.Date)
            .ToList();

        return grouped;
    }

    internal static (double AverageCycleTimeDays, IReadOnlyList<CycleTimeEntry> Entries) ComputeCycleTime(
        List<Card> cards,
        Column? doneColumn,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        if (doneColumn == null)
            return (0, Array.Empty<CycleTimeEntry>());

        // Cards that reached the done column within the date range
        var doneCards = cards
            .Where(c => c.ColumnId == doneColumn.Id
                        && c.UpdatedAt >= from
                        && c.UpdatedAt <= to)
            .ToList();

        if (doneCards.Count == 0)
            return (0, Array.Empty<CycleTimeEntry>());

        var entries = doneCards
            .Select(c =>
            {
                var cycleTime = (c.UpdatedAt - c.CreatedAt).TotalDays;
                return new CycleTimeEntry(c.Id, c.Title, Math.Round(cycleTime, 2));
            })
            .OrderBy(e => e.CycleTimeDays)
            .ToList();

        var avgCycleTime = Math.Round(entries.Average(e => e.CycleTimeDays), 2);

        return (avgCycleTime, entries);
    }

    internal static IReadOnlyList<WipSnapshot> ComputeWip(
        List<Column> columns,
        List<Card> cards)
    {
        return columns
            .OrderBy(c => c.Position)
            .Select(col => new WipSnapshot(
                col.Id,
                col.Name,
                cards.Count(c => c.ColumnId == col.Id),
                col.WipLimit))
            .ToList();
    }

    internal static (int BlockedCount, IReadOnlyList<BlockedCardSummary> BlockedCards) ComputeBlocked(
        List<Card> cards)
    {
        var blockedCards = cards
            .Where(c => c.IsBlocked)
            .Select(c =>
            {
                // Estimate blocked duration from UpdatedAt (when blocked was set) to now
                var blockedDuration = (DateTimeOffset.UtcNow - c.UpdatedAt).TotalDays;
                return new BlockedCardSummary(
                    c.Id,
                    c.Title,
                    c.BlockReason,
                    Math.Round(blockedDuration, 2));
            })
            .OrderByDescending(b => b.BlockedDurationDays)
            .ToList();

        return (blockedCards.Count, blockedCards);
    }
}
