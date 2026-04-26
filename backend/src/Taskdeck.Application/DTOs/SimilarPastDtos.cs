namespace Taskdeck.Application.DTOs;

/// <summary>
/// DTO for a single similar past decision.
/// </summary>
public record SimilarPastDecisionDto(
    string Serial,
    string Title,
    string Verdict,
    string Date);

/// <summary>
/// DTO for the aggregated similar past result.
/// </summary>
public record SimilarPastResultDto(
    IReadOnlyList<SimilarPastDecisionDto> Decisions,
    double ApplyRate);
