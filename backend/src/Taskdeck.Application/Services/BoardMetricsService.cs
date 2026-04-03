using System.Text.RegularExpressions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

public class BoardMetricsService : IBoardMetricsService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuthorizationService _authorizationService;

    /// <summary>
    /// Well-known column names that indicate "done" status, checked case-insensitively.
    /// The first match (by highest position among matches) wins.
    /// </summary>
    private static readonly string[] DoneColumnNames =
    {
        "done", "complete", "completed", "finished", "closed", "shipped", "released"
    };

    public BoardMetricsService(
        IUnitOfWork unitOfWork,
        IAuthorizationService authorizationService)
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

        if (query.From > query.To)
            return Result.Failure<BoardMetricsResponse>(ErrorCodes.ValidationError, "From date must be before To date");

        // Enforce read permission
        var canRead = await _authorizationService.CanReadBoardAsync(actingUserId, query.BoardId);
        if (!canRead.IsSuccess)
            return Result.Failure<BoardMetricsResponse>(canRead.ErrorCode, canRead.ErrorMessage);

        if (!canRead.Value)
            return Result.Failure<BoardMetricsResponse>(ErrorCodes.Forbidden, "You do not have permission to view metrics for this board");

        // Verify board exists
        var board = await _unitOfWork.Boards.GetByIdAsync(query.BoardId, cancellationToken);
        if (board == null)
            return Result.Failure<BoardMetricsResponse>(ErrorCodes.NotFound, "Board not found");

        // Load columns (always lightweight — small number per board)
        var columns = (await _unitOfWork.Columns.GetByBoardIdAsync(query.BoardId, cancellationToken)).ToList();

        // Determine the "done" column: prefer a column whose name matches known done patterns,
        // fall back to the rightmost column by position.
        var doneColumn = ResolveDoneColumn(columns);

        // Load audit logs for card moves in this board to determine actual completion timestamps
        var cardMoveAudits = await LoadCardMoveAuditsAsync(query.BoardId, query.From, query.To, cancellationToken);

        // SQL-level WIP: count cards per column without loading card entities
        var columnCounts = await _unitOfWork.Cards.CountCardsByColumnAsync(
            query.BoardId, query.LabelId, cancellationToken);
        var wipSnapshots = ComputeWipFromCounts(columns, columnCounts);
        var totalWip = wipSnapshots.Sum(w => w.CardCount);

        // SQL-level blocked cards: only load blocked cards, not the entire board
        var blockedCardEntities = (await _unitOfWork.Cards.GetBlockedByBoardIdAsync(
            query.BoardId, query.LabelId, cancellationToken)).ToList();
        var (blockedCount, blockedCards) = ComputeBlocked(blockedCardEntities);

        // For throughput and cycle time, only load cards actually needed:
        // 1. Cards referenced by audit move logs (for CreatedAt in cycle time computation)
        // 2. All board cards only as fallback when there are no audit-based done-moves
        //    (ComputeThroughput/ComputeCycleTime internally fall back to done-column
        //     cards when no audit data produces done-column matches)
        // This avoids loading the entire board into memory on the primary (audit) path.
        var auditCardIds = cardMoveAudits.Keys.ToHashSet();

        if (doneColumn != null)
        {
            List<Card> relevantCards;

            if (auditCardIds.Count > 0)
            {
                // Primary path: load only the specific cards referenced by audits
                relevantCards = (await _unitOfWork.Cards.GetForMetricsAsync(
                    query.BoardId, query.LabelId, auditCardIds, cancellationToken)).ToList();
            }
            else
            {
                // Fallback: no audit data in range — load all board cards so
                // ComputeThroughput/ComputeCycleTime can use the done-column fallback.
                // TODO(#675): push done-column + date-range filter to SQL to avoid
                // loading all cards when no audit data exists.
                relevantCards = (await _unitOfWork.Cards.GetForMetricsAsync(
                    query.BoardId, query.LabelId, cancellationToken: cancellationToken)).ToList();
            }

            var throughput = ComputeThroughput(relevantCards, doneColumn, query.From, query.To, cardMoveAudits);
            var (avgCycleTime, cycleTimeEntries) = ComputeCycleTime(relevantCards, doneColumn, query.From, query.To, cardMoveAudits);

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

        // No done column — throughput and cycle time are empty
        return Result.Success(new BoardMetricsResponse(
            query.BoardId,
            query.From,
            query.To,
            Array.Empty<ThroughputDataPoint>(),
            0,
            Array.Empty<CycleTimeEntry>(),
            wipSnapshots,
            totalWip,
            blockedCount,
            blockedCards));
    }

    /// <summary>
    /// Resolve the "done" column. Prefer columns whose name matches a well-known done pattern
    /// (case-insensitive). If multiple match, pick the one with the highest position.
    /// If none match, fall back to the rightmost column by position.
    /// </summary>
    internal static Column? ResolveDoneColumn(List<Column> columns)
    {
        if (columns.Count == 0) return null;

        var doneByName = columns
            .Where(c => DoneColumnNames.Any(name =>
                c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(c => c.Position)
            .FirstOrDefault();

        return doneByName ?? columns.OrderByDescending(c => c.Position).First();
    }

    /// <summary>
    /// Load audit log entries for card moves within the date range for this board.
    /// Returns a lookup: CardId -> list of (Timestamp, TargetColumnId).
    /// </summary>
    private async Task<Dictionary<Guid, List<(DateTimeOffset Timestamp, Guid TargetColumnId)>>> LoadCardMoveAuditsAsync(
        Guid boardId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var audits = await _unitOfWork.AuditLogs.QueryAsync(
            from, to,
            boardId: boardId,
            limit: 10000,
            cancellationToken: cancellationToken);

        var moveAudits = audits
            .Where(a => a.Action == AuditAction.Moved && a.EntityType == "card" && a.Changes != null);

        var result = new Dictionary<Guid, List<(DateTimeOffset, Guid)>>();

        foreach (var audit in moveAudits)
        {
            var targetColumnId = ParseTargetColumnId(audit.Changes!);
            if (targetColumnId == null) continue;

            if (!result.ContainsKey(audit.EntityId))
                result[audit.EntityId] = new List<(DateTimeOffset, Guid)>();

            result[audit.EntityId].Add((audit.Timestamp, targetColumnId.Value));
        }

        return result;
    }

    /// <summary>
    /// Parse the target column ID from audit log change text like "target_column=GUID; position=N".
    /// </summary>
    internal static Guid? ParseTargetColumnId(string changes)
    {
        var match = Regex.Match(changes, @"target_column=([0-9a-fA-F\-]{36})");
        if (match.Success && Guid.TryParse(match.Groups[1].Value, out var guid))
            return guid;
        return null;
    }

    internal static IReadOnlyList<ThroughputDataPoint> ComputeThroughput(
        List<Card> cards,
        Column? doneColumn,
        DateTimeOffset from,
        DateTimeOffset to,
        Dictionary<Guid, List<(DateTimeOffset Timestamp, Guid TargetColumnId)>> cardMoveAudits)
    {
        if (doneColumn == null)
            return Array.Empty<ThroughputDataPoint>();

        // Use audit logs to find cards that were moved to the done column within the date range.
        // Each audit entry for a card move to the done column counts as a completion.
        var completionDates = new List<DateTimeOffset>();

        foreach (var (cardId, moves) in cardMoveAudits)
        {
            foreach (var (timestamp, targetColumnId) in moves)
            {
                if (targetColumnId == doneColumn.Id)
                {
                    completionDates.Add(timestamp);
                }
            }
        }

        // If no audit data is available, fall back to cards currently in the done column
        // with UpdatedAt in range (less accurate but provides backward compatibility).
        if (completionDates.Count == 0)
        {
            var fallbackCards = cards
                .Where(c => c.ColumnId == doneColumn.Id
                            && c.UpdatedAt >= from
                            && c.UpdatedAt <= to)
                .ToList();

            completionDates.AddRange(fallbackCards.Select(c => c.UpdatedAt));
        }

        var grouped = completionDates
            .GroupBy(d => d.UtcDateTime.Date)
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
        DateTimeOffset to,
        Dictionary<Guid, List<(DateTimeOffset Timestamp, Guid TargetColumnId)>> cardMoveAudits)
    {
        if (doneColumn == null)
            return (0, Array.Empty<CycleTimeEntry>());

        var entries = new List<CycleTimeEntry>();

        // Use audit logs: find cards moved to done column, compute cycle time from creation.
        foreach (var (cardId, moves) in cardMoveAudits)
        {
            var doneMove = moves
                .Where(m => m.TargetColumnId == doneColumn.Id)
                .OrderBy(m => m.Timestamp)
                .FirstOrDefault();

            if (doneMove.Timestamp == default) continue;

            var card = cards.FirstOrDefault(c => c.Id == cardId);
            if (card == null) continue;

            var cycleTime = (doneMove.Timestamp - card.CreatedAt).TotalDays;
            entries.Add(new CycleTimeEntry(card.Id, card.Title, Math.Round(cycleTime, 2)));
        }

        // Fallback: if no audit data, use UpdatedAt-based calculation for backward compat
        if (entries.Count == 0)
        {
            var doneCards = cards
                .Where(c => c.ColumnId == doneColumn.Id
                            && c.UpdatedAt >= from
                            && c.UpdatedAt <= to)
                .ToList();

            entries = doneCards
                .Select(c =>
                {
                    var cycleTime = (c.UpdatedAt - c.CreatedAt).TotalDays;
                    return new CycleTimeEntry(c.Id, c.Title, Math.Round(cycleTime, 2));
                })
                .ToList();
        }

        if (entries.Count == 0)
            return (0, Array.Empty<CycleTimeEntry>());

        entries = entries.OrderBy(e => e.CycleTimeDays).ToList();
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

    /// <summary>
    /// Compute WIP snapshots from pre-aggregated SQL-level column counts,
    /// avoiding loading all card entities into memory.
    /// </summary>
    internal static IReadOnlyList<WipSnapshot> ComputeWipFromCounts(
        List<Column> columns,
        IReadOnlyList<(Guid ColumnId, int CardCount)> columnCounts)
    {
        var countLookup = columnCounts.ToDictionary(c => c.ColumnId, c => c.CardCount);

        return columns
            .OrderBy(c => c.Position)
            .Select(col => new WipSnapshot(
                col.Id,
                col.Name,
                countLookup.GetValueOrDefault(col.Id, 0),
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
                // Note: blocked duration is estimated from UpdatedAt. If the card was edited
                // after being blocked, this will underestimate the true blocked duration.
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
