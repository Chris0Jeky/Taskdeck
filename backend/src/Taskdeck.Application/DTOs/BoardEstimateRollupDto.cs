namespace Taskdeck.Application.DTOs;

public sealed record EstimateTotalsDto(int CardCount, long KnownEstimateMinutes, int MissingEstimateCount);

public sealed record ColumnEstimateRollupDto(Guid ColumnId, string Name, EstimateTotalsDto Totals);

public sealed record ParticipantEstimateRollupDto(Guid UserId, string Username, EstimateTotalsDto Totals);

/// <summary>
/// Current active-card estimates. Each card appears once in Board and its column.
/// A multi-assigned card contributes its full estimate to each participant, so
/// participant totals overlap and must never be added to calculate the board total.
/// Parent estimates remain independent of their children's estimates.
/// </summary>
public sealed record BoardEstimateRollupDto(
    Guid BoardId,
    DateTimeOffset GeneratedAt,
    EstimateTotalsDto Board,
    IReadOnlyList<ColumnEstimateRollupDto> Columns,
    IReadOnlyList<ParticipantEstimateRollupDto> Participants,
    EstimateTotalsDto Unassigned);
