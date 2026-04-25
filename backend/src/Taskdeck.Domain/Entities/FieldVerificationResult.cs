using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// Result of verifying an extractive quote or inferred field against source material.
/// This is a value object -- immutable and identity-free.
/// </summary>
public sealed class FieldVerificationResult
{
    /// <summary>
    /// The name of the field that was verified.
    /// </summary>
    public string FieldName { get; }

    /// <summary>
    /// The verification outcome.
    /// </summary>
    public VerificationStatus Status { get; }

    /// <summary>
    /// Similarity score produced by the fuzzy matcher. Range [0.0, 1.0].
    /// Null when verification was not attempted (e.g., source not found).
    /// </summary>
    public double? SimilarityScore { get; }

    /// <summary>
    /// Original confidence before verification adjustments.
    /// </summary>
    public double OriginalConfidence { get; }

    /// <summary>
    /// Adjusted confidence after verification.
    /// For Verified status, this equals OriginalConfidence.
    /// For Downgraded, this is reduced proportionally.
    /// For Failed, this is set to 0.0.
    /// </summary>
    public double AdjustedConfidence { get; }

    /// <summary>
    /// Human-readable explanation of the verification result.
    /// </summary>
    public string? Reason { get; }

    public FieldVerificationResult(
        string fieldName,
        VerificationStatus status,
        double originalConfidence,
        double adjustedConfidence,
        double? similarityScore = null,
        string? reason = null)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
            throw new DomainException(ErrorCodes.ValidationError, "FieldName cannot be empty");
        if (!Enum.IsDefined(status))
            throw new DomainException(ErrorCodes.ValidationError, "VerificationStatus value is invalid");
        if (originalConfidence < 0.0 || originalConfidence > 1.0)
            throw new DomainException(ErrorCodes.ValidationError, "OriginalConfidence must be between 0.0 and 1.0");
        if (adjustedConfidence < 0.0 || adjustedConfidence > 1.0)
            throw new DomainException(ErrorCodes.ValidationError, "AdjustedConfidence must be between 0.0 and 1.0");
        if (similarityScore.HasValue && (similarityScore.Value < 0.0 || similarityScore.Value > 1.0))
            throw new DomainException(ErrorCodes.ValidationError, "SimilarityScore must be between 0.0 and 1.0");

        // Enforce verification-status/confidence consistency.
        // The contract (see AdjustedConfidence doc) requires:
        //   Verified   -> AdjustedConfidence == OriginalConfidence
        //   Downgraded -> AdjustedConfidence < OriginalConfidence
        //   Failed     -> AdjustedConfidence == 0.0
        //   Unverified -> no constraint (not yet processed)
        switch (status)
        {
            case VerificationStatus.Verified:
                if (adjustedConfidence != originalConfidence)
                    throw new DomainException(ErrorCodes.ValidationError,
                        "AdjustedConfidence must equal OriginalConfidence for Verified status");
                break;
            case VerificationStatus.Downgraded:
                if (adjustedConfidence >= originalConfidence)
                    throw new DomainException(ErrorCodes.ValidationError,
                        "AdjustedConfidence must be less than OriginalConfidence for Downgraded status");
                break;
            case VerificationStatus.Failed:
                if (adjustedConfidence != 0.0)
                    throw new DomainException(ErrorCodes.ValidationError,
                        "AdjustedConfidence must be 0.0 for Failed status");
                break;
        }

        FieldName = fieldName;
        Status = status;
        OriginalConfidence = originalConfidence;
        AdjustedConfidence = adjustedConfidence;
        SimilarityScore = similarityScore;
        Reason = reason;
    }

    public override bool Equals(object? obj)
    {
        if (obj is not FieldVerificationResult other)
            return false;

        return FieldName == other.FieldName
            && Status == other.Status
            && OriginalConfidence == other.OriginalConfidence
            && AdjustedConfidence == other.AdjustedConfidence
            && SimilarityScore == other.SimilarityScore
            && Reason == other.Reason;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(FieldName, Status, OriginalConfidence, AdjustedConfidence, SimilarityScore, Reason);
    }
}
