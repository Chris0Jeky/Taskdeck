using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Entities;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Services;

public class FieldVerifier : IFieldVerifier
{
    private readonly IFuzzyTextMatcher _fuzzyMatcher;

    private const double VerifiedThreshold = 0.85;
    private const double DowngradedThreshold = 0.5;

    public FieldVerifier(IFuzzyTextMatcher fuzzyMatcher)
    {
        _fuzzyMatcher = fuzzyMatcher ?? throw new ArgumentNullException(nameof(fuzzyMatcher));
    }

    public FieldVerificationResult VerifyExtractiveField(
        string fieldName,
        string extractiveQuote,
        string sourceText,
        double originalConfidence)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
            throw new ArgumentException("fieldName cannot be empty", nameof(fieldName));
        if (string.IsNullOrWhiteSpace(extractiveQuote))
            throw new ArgumentException("extractiveQuote cannot be empty", nameof(extractiveQuote));

        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return new FieldVerificationResult(
                fieldName,
                VerificationStatus.Failed,
                originalConfidence,
                adjustedConfidence: 0.0,
                similarityScore: null,
                reason: "Source text is empty or unavailable");
        }

        var similarity = _fuzzyMatcher.ComputeSimilarity(extractiveQuote, sourceText);

        if (similarity >= VerifiedThreshold)
        {
            return new FieldVerificationResult(
                fieldName,
                VerificationStatus.Verified,
                originalConfidence,
                adjustedConfidence: originalConfidence,
                similarityScore: similarity,
                reason: null);
        }

        if (similarity >= DowngradedThreshold)
        {
            var adjustedConfidence = originalConfidence * similarity;
            return new FieldVerificationResult(
                fieldName,
                VerificationStatus.Downgraded,
                originalConfidence,
                adjustedConfidence: adjustedConfidence,
                similarityScore: similarity,
                reason: $"Partial match ({similarity:F2}) below verification threshold ({VerifiedThreshold:F2})");
        }

        return new FieldVerificationResult(
            fieldName,
            VerificationStatus.Failed,
            originalConfidence,
            adjustedConfidence: 0.0,
            similarityScore: similarity,
            reason: $"Quote not found in source (similarity {similarity:F2} below minimum {DowngradedThreshold:F2})");
    }

    public FieldVerificationResult VerifyInferredField(
        string fieldName,
        IReadOnlyList<ProvenanceEvidenceLink> evidenceLinks,
        IReadOnlyList<SourceBlock> availableSourceBlocks,
        double originalConfidence)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
            throw new ArgumentException("fieldName cannot be empty", nameof(fieldName));
        ArgumentNullException.ThrowIfNull(evidenceLinks);
        ArgumentNullException.ThrowIfNull(availableSourceBlocks);

        if (evidenceLinks.Count == 0)
        {
            return new FieldVerificationResult(
                fieldName,
                VerificationStatus.Failed,
                originalConfidence,
                adjustedConfidence: 0.0,
                similarityScore: null,
                reason: "No evidence links provided for inferred field");
        }

        var resolvedCount = 0;
        foreach (var link in evidenceLinks)
        {
            var sourceBlock = availableSourceBlocks.FirstOrDefault(
                b => b.Id.ToString() == link.SourceId || b.SourceReferenceId == link.SourceId);

            if (sourceBlock == null)
                continue;

            if (link.SpanStart.HasValue && link.SpanEnd.HasValue)
            {
                if (link.SpanStart.Value >= 0 &&
                    link.SpanEnd.Value <= sourceBlock.Content.Length &&
                    link.SpanEnd.Value > link.SpanStart.Value)
                {
                    resolvedCount++;
                }
            }
            else
            {
                resolvedCount++;
            }
        }

        var resolutionRate = (double)resolvedCount / evidenceLinks.Count;

        if (resolutionRate >= 1.0)
        {
            return new FieldVerificationResult(
                fieldName,
                VerificationStatus.Verified,
                originalConfidence,
                adjustedConfidence: originalConfidence,
                similarityScore: resolutionRate,
                reason: null);
        }

        if (resolutionRate > 0.0)
        {
            var adjustedConfidence = originalConfidence * resolutionRate;
            return new FieldVerificationResult(
                fieldName,
                VerificationStatus.Downgraded,
                originalConfidence,
                adjustedConfidence: adjustedConfidence,
                similarityScore: resolutionRate,
                reason: $"Only {resolvedCount}/{evidenceLinks.Count} evidence links resolved");
        }

        return new FieldVerificationResult(
            fieldName,
            VerificationStatus.Failed,
            originalConfidence,
            adjustedConfidence: 0.0,
            similarityScore: 0.0,
            reason: "No evidence links could be resolved against available source blocks");
    }
}
