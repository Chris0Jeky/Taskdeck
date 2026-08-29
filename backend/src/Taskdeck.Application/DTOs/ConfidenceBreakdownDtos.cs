namespace Taskdeck.Application.DTOs;

/// <summary>
/// DTO representing a single named component of a confidence breakdown.
/// </summary>
public record ConfidenceComponentDto(
    string Key,
    double Value);

/// <summary>
/// DTO representing confidence values actually recorded with proposal provenance. Components are
/// per-item values, never heuristic scores synthesized from proposal metadata.
/// </summary>
public record ConfidenceBreakdownDto(
    double? Overall,
    IReadOnlyList<ConfidenceComponentDto> Components,
    string? Note,
    double? Threshold,
    string Source)
{
    public const string ModelReportedSource = "model-reported";
    public const string DeterministicSource = "deterministic";
    public const string DerivedSource = "derived";
    public const string NotReportedSource = "not-reported";

    /// <summary>
    /// Retained as a nullable compatibility field. Confidence has no apply threshold and never
    /// authorizes a write, so this is always null.
    /// </summary>
    public bool? MeetsThreshold => null;
}
