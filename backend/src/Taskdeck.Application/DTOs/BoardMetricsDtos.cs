namespace Taskdeck.Application.DTOs;

/// <summary>
/// Query parameters for board metrics.
/// </summary>
public sealed record BoardMetricsQuery(
    Guid BoardId,
    DateTimeOffset From,
    DateTimeOffset To,
    Guid? LabelId = null);

/// <summary>
/// Throughput data point: number of cards completed in a time bucket.
/// </summary>
public sealed record ThroughputDataPoint(
    DateTimeOffset Date,
    int CompletedCount);

/// <summary>
/// Cycle time entry: how long a card took from creation to reaching the
/// final (rightmost) column.
/// </summary>
public sealed record CycleTimeEntry(
    Guid CardId,
    string CardTitle,
    double CycleTimeDays);

/// <summary>
/// Snapshot of work-in-progress across columns.
/// </summary>
public sealed record WipSnapshot(
    Guid ColumnId,
    string ColumnName,
    int CardCount,
    int? WipLimit);

/// <summary>
/// Blocked card summary.
/// </summary>
public sealed record BlockedCardSummary(
    Guid CardId,
    string CardTitle,
    string? BlockReason,
    double BlockedDurationDays);

/// <summary>
/// Aggregate response containing all board metrics.
/// </summary>
public sealed record BoardMetricsResponse(
    Guid BoardId,
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ThroughputDataPoint> Throughput,
    double AverageCycleTimeDays,
    IReadOnlyList<CycleTimeEntry> CycleTimeEntries,
    IReadOnlyList<WipSnapshot> WipSnapshots,
    int TotalWip,
    int BlockedCount,
    IReadOnlyList<BlockedCardSummary> BlockedCards);
