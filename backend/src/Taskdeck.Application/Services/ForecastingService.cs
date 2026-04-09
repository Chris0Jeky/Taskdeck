using System.Text.RegularExpressions;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services;

/// <summary>
/// Heuristic forecasting service that estimates board completion dates
/// using rolling throughput averages and standard-deviation confidence bands.
/// All calculations are deterministic and explainable — no ML.
/// </summary>
public class ForecastingService : IForecastingService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuthorizationService _authorizationService;

    /// <summary>Default history window when not specified by the caller.</summary>
    internal const int DefaultHistoryDays = 30;

    /// <summary>Minimum data points required for a meaningful confidence band.</summary>
    internal const int MinDataPointsForConfidenceBand = 3;

    /// <summary>
    /// Performance budget: maximum audit log entries to process per forecast.
    /// Prevents unbounded memory growth on boards with very long histories.
    /// </summary>
    internal const int MaxAuditEntries = 10_000;

    /// <summary>
    /// Well-known column names that indicate "done" status, checked case-insensitively.
    /// Matches BoardMetricsService for consistency.
    /// </summary>
    private static readonly string[] DoneColumnNames =
    {
        "done", "complete", "completed", "finished", "closed", "shipped", "released"
    };

    /// <summary>
    /// Compiled regex for extracting target_column GUID from audit log change strings.
    /// Static to avoid recompilation per audit entry (up to MaxAuditEntries per forecast).
    /// </summary>
    private static readonly Regex TargetColumnRegex =
        new(@"target_column=([0-9a-fA-F\-]{36})", RegexOptions.Compiled);

    public ForecastingService(
        IUnitOfWork unitOfWork,
        IAuthorizationService authorizationService)
    {
        _unitOfWork = unitOfWork;
        _authorizationService = authorizationService;
    }

    public async Task<Result<BoardForecastResponse>> GetBoardForecastAsync(
        BoardForecastQuery query,
        Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        // --- Validation ---
        if (query.BoardId == Guid.Empty)
            return Result.Failure<BoardForecastResponse>(ErrorCodes.ValidationError, "Board ID is required");

        if (actingUserId == Guid.Empty)
            return Result.Failure<BoardForecastResponse>(ErrorCodes.ValidationError, "Acting user ID cannot be empty");

        if (query.HistoryDays is < 1 or > 365)
            return Result.Failure<BoardForecastResponse>(ErrorCodes.ValidationError, "History days must be between 1 and 365");

        // --- Authorization ---
        var canRead = await _authorizationService.CanReadBoardAsync(actingUserId, query.BoardId);
        if (!canRead.IsSuccess)
            return Result.Failure<BoardForecastResponse>(canRead.ErrorCode, canRead.ErrorMessage);

        if (!canRead.Value)
            return Result.Failure<BoardForecastResponse>(ErrorCodes.Forbidden, "You do not have permission to view forecasts for this board");

        // --- Board existence ---
        var board = await _unitOfWork.Boards.GetByIdAsync(query.BoardId, cancellationToken);
        if (board == null)
            return Result.Failure<BoardForecastResponse>(ErrorCodes.NotFound, "Board not found");

        // --- Resolve columns and done column ---
        var columns = (await _unitOfWork.Columns.GetByBoardIdAsync(query.BoardId, cancellationToken)).ToList();
        var doneColumn = ResolveDoneColumn(columns);

        var historyDays = query.HistoryDays ?? DefaultHistoryDays;
        var now = DateTimeOffset.UtcNow;
        var historyFrom = now.AddDays(-historyDays);

        var assumptions = new List<string>();
        var caveats = new List<string>();

        // --- No columns: return empty forecast ---
        if (doneColumn == null)
        {
            caveats.Add("Board has no columns; forecasting is not possible.");
            return Result.Success(EmptyForecast(query.BoardId, historyDays, assumptions, caveats));
        }

        // --- Load audit data for throughput ---
        var cardMoveAudits = await LoadCardMoveAuditsAsync(
            query.BoardId, historyFrom, now, cancellationToken);

        // --- Compute daily throughput from audit data ---
        var dailyThroughput = ComputeDailyThroughput(
            cardMoveAudits, doneColumn.Id, historyFrom, now);

        // --- Count remaining (non-done) and completed cards ---
        var columnCounts = await _unitOfWork.Cards.CountCardsByColumnAsync(
            query.BoardId, cancellationToken: cancellationToken);

        int completedCards = 0;
        int remainingCards = 0;
        foreach (var (columnId, cardCount) in columnCounts)
        {
            if (columnId == doneColumn.Id)
                completedCards += cardCount;
            else
                remainingCards += cardCount;
        }

        // --- Compute statistics ---
        var (avgThroughput, stdDev) = ComputeThroughputStatistics(dailyThroughput, historyDays);
        var dataPointCount = dailyThroughput.Count;

        // --- Compute average cycle time from recent completions ---
        var avgCycleTime = await ComputeAverageCycleTimeAsync(
            query.BoardId, doneColumn, cardMoveAudits, cancellationToken);

        // --- Build assumptions ---
        assumptions.Add($"Uses {historyDays}-day rolling history window");
        assumptions.Add("Throughput calculated as cards moved to done column per day");
        assumptions.Add("Cycle time measured from card creation to done-column arrival");
        assumptions.Add("Future throughput assumed to match historical average (constant rate)");

        if (doneColumn.Name.Equals(columns.OrderByDescending(c => c.Position).First().Name))
        {
            if (!DoneColumnNames.Any(n => doneColumn.Name.Equals(n, StringComparison.OrdinalIgnoreCase)))
                assumptions.Add($"Done column resolved to rightmost column '{doneColumn.Name}' (no well-known done name found)");
        }

        // --- Edge cases ---
        if (remainingCards == 0)
        {
            caveats.Add("No remaining cards — the board appears complete.");
            return Result.Success(new BoardForecastResponse(
                query.BoardId,
                RemainingCards: 0,
                CompletedCards: completedCards,
                AverageThroughputPerDay: Math.Round(avgThroughput, 4),
                ThroughputStdDev: Math.Round(stdDev, 4),
                AverageCycleTimeDays: Math.Round(avgCycleTime, 2),
                EstimatedCompletionDate: now,
                ConfidenceBand: null,
                DataPointCount: dataPointCount,
                HistoryDaysUsed: historyDays,
                Assumptions: assumptions,
                Caveats: caveats));
        }

        if (avgThroughput <= 0)
        {
            caveats.Add("Zero throughput in the history window — no cards were completed, so completion cannot be estimated.");
            if (dataPointCount == 0)
                caveats.Add("No completion data points found in the specified history window.");
            return Result.Success(new BoardForecastResponse(
                query.BoardId,
                RemainingCards: remainingCards,
                CompletedCards: completedCards,
                AverageThroughputPerDay: 0,
                ThroughputStdDev: 0,
                AverageCycleTimeDays: Math.Round(avgCycleTime, 2),
                EstimatedCompletionDate: null,
                ConfidenceBand: null,
                DataPointCount: dataPointCount,
                HistoryDaysUsed: historyDays,
                Assumptions: assumptions,
                Caveats: caveats));
        }

        // --- Estimate completion ---
        var daysToComplete = remainingCards / avgThroughput;
        var estimatedCompletion = now.AddDays(daysToComplete);

        // --- Confidence band ---
        ConfidenceBand? confidenceBand = null;
        if (dataPointCount >= MinDataPointsForConfidenceBand && stdDev > 0)
        {
            var optimisticThroughput = avgThroughput + stdDev;
            var pessimisticThroughput = Math.Max(avgThroughput - stdDev, 0.001); // floor to avoid division by zero

            var optimisticDays = remainingCards / optimisticThroughput;
            var pessimisticDays = remainingCards / pessimisticThroughput;

            confidenceBand = new ConfidenceBand(
                LowEstimate: now.AddDays(optimisticDays),
                ExpectedEstimate: estimatedCompletion,
                HighEstimate: now.AddDays(pessimisticDays),
                LowThroughputPerDay: Math.Round(pessimisticThroughput, 4),
                ExpectedThroughputPerDay: Math.Round(avgThroughput, 4),
                HighThroughputPerDay: Math.Round(optimisticThroughput, 4));

            if (stdDev > avgThroughput)
                caveats.Add("High throughput variance — confidence band is wide, estimate may be unreliable.");
        }
        else
        {
            if (dataPointCount < MinDataPointsForConfidenceBand)
                caveats.Add($"Only {dataPointCount} data point(s) — insufficient for confidence bands (need {MinDataPointsForConfidenceBand}+).");
            else
                caveats.Add("Zero throughput variance — all days had identical throughput, confidence band not applicable.");
        }

        if (dataPointCount < 7)
            caveats.Add("Limited history — forecast accuracy improves with more completed cards.");

        return Result.Success(new BoardForecastResponse(
            query.BoardId,
            RemainingCards: remainingCards,
            CompletedCards: completedCards,
            AverageThroughputPerDay: Math.Round(avgThroughput, 4),
            ThroughputStdDev: Math.Round(stdDev, 4),
            AverageCycleTimeDays: Math.Round(avgCycleTime, 2),
            EstimatedCompletionDate: estimatedCompletion,
            ConfidenceBand: confidenceBand,
            DataPointCount: dataPointCount,
            HistoryDaysUsed: historyDays,
            Assumptions: assumptions,
            Caveats: caveats));
    }

    // --- Internal helpers (visible for testing) ---

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
    /// Compute daily throughput: for each day in the history window,
    /// count how many cards were moved to the done column.
    /// Only counts the *last* move to done per card to avoid inflating throughput
    /// when cards bounce between columns (e.g. Done → In Progress → Done).
    /// Only includes days with at least one completion (sparse representation).
    /// </summary>
    internal static List<DailyThroughputPoint> ComputeDailyThroughput(
        Dictionary<Guid, List<(DateTimeOffset Timestamp, Guid TargetColumnId)>> cardMoveAudits,
        Guid doneColumnId,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var completionsByDay = new Dictionary<DateTime, int>();

        foreach (var (_, moves) in cardMoveAudits)
        {
            // Only count the last move to done per card to avoid double-counting
            // cards that bounced between columns.
            var lastDoneMove = moves
                .Where(m => m.TargetColumnId == doneColumnId)
                .OrderByDescending(m => m.Timestamp)
                .FirstOrDefault();

            if (lastDoneMove.Timestamp == default) continue;

            var day = lastDoneMove.Timestamp.UtcDateTime.Date;
            completionsByDay.TryGetValue(day, out var count);
            completionsByDay[day] = count + 1;
        }

        return completionsByDay
            .Select(kvp => new DailyThroughputPoint(kvp.Key, kvp.Value))
            .OrderBy(p => p.Date)
            .ToList();
    }

    /// <summary>
    /// Compute mean and standard deviation of daily throughput.
    /// Uses the full history window (including zero-throughput days) as the denominator
    /// for an accurate per-day average, preventing inflated rates when completions
    /// are bursty within a longer observation period.
    /// </summary>
    /// <param name="dailyThroughput">Sparse daily throughput points (only days with completions).</param>
    /// <param name="historyWindowDays">
    /// Total number of days in the requested history window. Used as the denominator
    /// for mean and std dev to avoid inflating rates when completions cluster.
    /// </param>
    internal static (double Mean, double StdDev) ComputeThroughputStatistics(
        List<DailyThroughputPoint> dailyThroughput,
        int historyWindowDays)
    {
        if (dailyThroughput.Count == 0 || historyWindowDays <= 0)
            return (0, 0);

        var spanDays = Math.Max(historyWindowDays, 1);

        // Total completions over the window
        var totalCompletions = dailyThroughput.Sum(d => d.Count);

        var mean = (double)totalCompletions / spanDays;

        if (spanDays == 1)
            return (mean, 0);

        // Compute std dev over the full history window (including zero days).
        // Use dictionary for O(1) lookup per day.
        var countByDate = dailyThroughput.ToDictionary(d => d.Date, d => d.Count);

        // Sum squared differences for days that have data
        var sumSquaredDiff = 0.0;
        int daysAccountedFor = 0;
        foreach (var kvp in countByDate)
        {
            var diff = kvp.Value - mean;
            sumSquaredDiff += diff * diff;
            daysAccountedFor++;
        }
        // Add squared differences for all zero-throughput days
        var zeroDays = spanDays - daysAccountedFor;
        sumSquaredDiff += zeroDays * (mean * mean);

        var variance = sumSquaredDiff / spanDays; // population std dev (not sample)
        var stdDev = Math.Sqrt(variance);

        return (mean, stdDev);
    }

    /// <summary>
    /// Compute average cycle time for cards that were moved to the done column
    /// in the history window.
    /// </summary>
    private async Task<double> ComputeAverageCycleTimeAsync(
        Guid boardId,
        Column doneColumn,
        Dictionary<Guid, List<(DateTimeOffset Timestamp, Guid TargetColumnId)>> cardMoveAudits,
        CancellationToken cancellationToken)
    {
        // Find card IDs that were moved to done
        var doneCardIds = new HashSet<Guid>();
        var doneTimestamps = new Dictionary<Guid, DateTimeOffset>();

        foreach (var (cardId, moves) in cardMoveAudits)
        {
            var doneMove = moves
                .Where(m => m.TargetColumnId == doneColumn.Id)
                .OrderBy(m => m.Timestamp)
                .FirstOrDefault();

            if (doneMove.Timestamp != default)
            {
                doneCardIds.Add(cardId);
                doneTimestamps[cardId] = doneMove.Timestamp;
            }
        }

        if (doneCardIds.Count == 0)
            return 0;

        // Load the specific cards to get their CreatedAt
        var cards = (await _unitOfWork.Cards.GetForMetricsAsync(
            boardId, cardIds: doneCardIds, cancellationToken: cancellationToken)).ToList();

        var cycleTimes = new List<double>();
        foreach (var card in cards)
        {
            if (doneTimestamps.TryGetValue(card.Id, out var doneTime))
            {
                var cycleTime = (doneTime - card.CreatedAt).TotalDays;
                if (cycleTime >= 0) // guard against clock skew
                    cycleTimes.Add(cycleTime);
            }
        }

        return cycleTimes.Count > 0 ? cycleTimes.Average() : 0;
    }

    private async Task<Dictionary<Guid, List<(DateTimeOffset Timestamp, Guid TargetColumnId)>>> LoadCardMoveAuditsAsync(
        Guid boardId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var audits = await _unitOfWork.AuditLogs.QueryAsync(
            from, to,
            boardId: boardId,
            limit: MaxAuditEntries,
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

    internal static Guid? ParseTargetColumnId(string changes)
    {
        var match = TargetColumnRegex.Match(changes);
        if (match.Success && Guid.TryParse(match.Groups[1].Value, out var guid))
            return guid;
        return null;
    }

    private static BoardForecastResponse EmptyForecast(
        Guid boardId, int historyDays,
        List<string> assumptions, List<string> caveats)
    {
        return new BoardForecastResponse(
            boardId,
            RemainingCards: 0,
            CompletedCards: 0,
            AverageThroughputPerDay: 0,
            ThroughputStdDev: 0,
            AverageCycleTimeDays: 0,
            EstimatedCompletionDate: null,
            ConfidenceBand: null,
            DataPointCount: 0,
            HistoryDaysUsed: historyDays,
            Assumptions: assumptions,
            Caveats: caveats);
    }

    /// <summary>Internal record for daily throughput tracking.</summary>
    internal sealed record DailyThroughputPoint(DateTime Date, int Count);
}
