namespace Taskdeck.Application.DTOs;

/// <summary>
/// Query parameters for board forecasting.
/// </summary>
public sealed record BoardForecastQuery(
    Guid BoardId,
    int? HistoryDays = null);

/// <summary>
/// Confidence band for estimated completion: low/expected/high scenario dates.
/// Low = optimistic (avg + 1σ throughput), High = pessimistic (avg - 1σ throughput).
/// </summary>
public sealed record ConfidenceBand(
    DateTimeOffset? LowEstimate,
    DateTimeOffset ExpectedEstimate,
    DateTimeOffset? HighEstimate,
    double LowThroughputPerDay,
    double ExpectedThroughputPerDay,
    double HighThroughputPerDay);

/// <summary>
/// Complete forecast response for a board.
/// </summary>
public sealed record BoardForecastResponse(
    Guid BoardId,
    int RemainingCards,
    int CompletedCards,
    double AverageThroughputPerDay,
    double ThroughputStdDev,
    double AverageCycleTimeDays,
    DateTimeOffset? EstimatedCompletionDate,
    ConfidenceBand? ConfidenceBand,
    int DataPointCount,
    int HistoryDaysUsed,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> Caveats);
