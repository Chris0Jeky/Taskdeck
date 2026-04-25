using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Confidence;

/// <summary>
/// Policy governing when self-consistency checks should be triggered.
/// Self-consistency re-generates a proposal multiple times and compares results
/// to detect hallucination or instability.
/// </summary>
public sealed class SelfConsistencyPolicy
{
    /// <summary>
    /// Minimum criticality level (as a ConfidenceBucket) below which self-consistency is triggered.
    /// For example, if set to Medium, proposals at Medium criticality or above trigger self-consistency.
    /// </summary>
    public ConfidenceBucket CriticalityThreshold { get; }

    /// <summary>
    /// Confidence floor: if any field's aggregated confidence falls below this score,
    /// self-consistency is triggered regardless of criticality.
    /// </summary>
    public double ConfidenceFloor { get; }

    /// <summary>
    /// Number of independent generations to produce for self-consistency comparison.
    /// </summary>
    public int GenerationCount { get; }

    public SelfConsistencyPolicy(
        ConfidenceBucket criticalityThreshold,
        double confidenceFloor,
        int generationCount = 3)
    {
        if (double.IsNaN(confidenceFloor) || double.IsInfinity(confidenceFloor))
            throw new DomainException(ErrorCodes.ValidationError,
                "Confidence floor must be a finite number.");

        if (confidenceFloor < 0.0 || confidenceFloor > 1.0)
            throw new DomainException(ErrorCodes.ValidationError,
                $"Confidence floor must be between 0.0 and 1.0, but was {confidenceFloor}.");

        if (generationCount < 2)
            throw new DomainException(ErrorCodes.ValidationError,
                "Self-consistency requires at least 2 generations to compare.");

        CriticalityThreshold = criticalityThreshold;
        ConfidenceFloor = confidenceFloor;
        GenerationCount = generationCount;
    }

    /// <summary>
    /// Determines whether self-consistency should be triggered for a given proposal criticality
    /// and minimum field confidence.
    /// </summary>
    /// <param name="proposalCriticality">The criticality bucket of the proposal.</param>
    /// <param name="minimumFieldConfidence">The lowest field-level confidence in the proposal.</param>
    /// <returns>True if self-consistency checks should run.</returns>
    public bool ShouldTrigger(ConfidenceBucket proposalCriticality, double minimumFieldConfidence)
    {
        if (double.IsNaN(minimumFieldConfidence) || double.IsInfinity(minimumFieldConfidence))
            throw new DomainException(ErrorCodes.ValidationError,
                "Minimum field confidence must be a finite number.");

        if (minimumFieldConfidence < 0.0 || minimumFieldConfidence > 1.0)
            throw new DomainException(ErrorCodes.ValidationError,
                $"Minimum field confidence must be between 0.0 and 1.0, but was {minimumFieldConfidence}.");

        // Trigger if criticality meets or exceeds the threshold
        if (proposalCriticality >= CriticalityThreshold)
            return true;

        // Trigger if any field confidence is below the floor
        if (minimumFieldConfidence < ConfidenceFloor)
            return true;

        return false;
    }

    public override string ToString() =>
        $"SelfConsistencyPolicy(threshold={CriticalityThreshold}, floor={ConfidenceFloor:F2}, generations={GenerationCount})";
}
