namespace Taskdeck.Application.DTOs;

/// <summary>
/// DTO representing a single named component of a confidence breakdown.
/// </summary>
public record ConfidenceComponentDto(
    string Key,
    double Value);

/// <summary>
/// DTO representing the full multi-component confidence breakdown for a proposal.
/// </summary>
public record ConfidenceBreakdownDto(
    double Overall,
    IReadOnlyList<ConfidenceComponentDto> Components,
    string? Note,
    double Threshold)
{
    /// <summary>
    /// True when the overall confidence meets or exceeds the threshold.
    /// </summary>
    public bool MeetsThreshold => Overall >= Threshold;
}
