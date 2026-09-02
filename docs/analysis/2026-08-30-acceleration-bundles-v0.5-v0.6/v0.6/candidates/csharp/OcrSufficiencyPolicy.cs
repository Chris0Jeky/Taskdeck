
namespace Taskdeck.Acceleration.V06;

public sealed record OcrQualityFacts(
    int NonWhitespaceCharacters,
    int RegionCount,
    double? MeanConfidence,
    double? LowConfidenceRegionRatio,
    bool LayoutDependent,
    bool DiagramLike,
    bool TruncatedByLimit);

public sealed record OcrSufficiencyDecision(
    bool IsSufficient,
    IReadOnlyList<string> ReasonCodes);

public sealed record OcrSufficiencyThresholds(
    int MinimumCharacters,
    double MinimumMeanConfidence,
    double MaximumLowConfidenceRegionRatio);

public static class OcrSufficiencyPolicy
{
    public static OcrSufficiencyDecision Evaluate(
        OcrQualityFacts facts,
        OcrSufficiencyThresholds thresholds)
    {
        var reasons = new List<string>();
        if (facts.TruncatedByLimit) reasons.Add("ocr.output-truncated");
        if (facts.NonWhitespaceCharacters < thresholds.MinimumCharacters)
            reasons.Add("ocr.text-too-sparse");
        if (facts.MeanConfidence.HasValue &&
            facts.MeanConfidence.Value < thresholds.MinimumMeanConfidence)
            reasons.Add("ocr.mean-confidence-low");
        if (facts.LowConfidenceRegionRatio.HasValue &&
            facts.LowConfidenceRegionRatio.Value > thresholds.MaximumLowConfidenceRegionRatio)
            reasons.Add("ocr.low-confidence-coverage-high");
        if (facts.LayoutDependent) reasons.Add("ocr.layout-dependent");
        if (facts.DiagramLike) reasons.Add("ocr.diagram-like");

        return new OcrSufficiencyDecision(reasons.Count == 0, reasons);
    }
}
