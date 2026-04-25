using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Application.Services.Confidence;

/// <summary>
/// Calculates Brier scores to measure calibration quality of confidence predictions.
/// Brier score = (1/N) * sum((predicted_i - outcome_i)^2)
/// where predicted_i is the confidence (probability) and outcome_i is 0 or 1.
///
/// Perfect predictions yield a Brier score of 0.0.
/// Worst predictions yield a Brier score of 1.0.
/// </summary>
public static class BrierScoreCalculator
{
    /// <summary>
    /// A single prediction-outcome pair for Brier score computation.
    /// </summary>
    /// <param name="PredictedProbability">The predicted probability [0.0, 1.0].</param>
    /// <param name="ActualOutcome">The actual outcome: true (1) or false (0).</param>
    public readonly record struct Prediction(double PredictedProbability, bool ActualOutcome);

    /// <summary>
    /// Computes the Brier score for a set of predictions.
    /// </summary>
    /// <param name="predictions">The prediction-outcome pairs.</param>
    /// <returns>The Brier score in [0.0, 1.0].</returns>
    /// <exception cref="DomainException">
    /// Thrown if predictions is empty or any predicted probability is outside [0.0, 1.0].
    /// </exception>
    public static double Calculate(IReadOnlyList<Prediction> predictions)
    {
        if (predictions is null || predictions.Count == 0)
            throw new DomainException(ErrorCodes.ValidationError,
                "At least one prediction is required to calculate a Brier score.");

        double sumSquaredError = 0.0;

        for (int i = 0; i < predictions.Count; i++)
        {
            var p = predictions[i];

            if (double.IsNaN(p.PredictedProbability) || double.IsInfinity(p.PredictedProbability))
                throw new DomainException(ErrorCodes.ValidationError,
                    $"Prediction {i}: predicted probability must be a finite number.");

            if (p.PredictedProbability < 0.0 || p.PredictedProbability > 1.0)
                throw new DomainException(ErrorCodes.ValidationError,
                    $"Prediction {i}: predicted probability must be between 0.0 and 1.0, but was {p.PredictedProbability}.");

            double outcome = p.ActualOutcome ? 1.0 : 0.0;
            double error = p.PredictedProbability - outcome;
            sumSquaredError += error * error;
        }

        return sumSquaredError / predictions.Count;
    }

    /// <summary>
    /// Computes the Brier skill score relative to a reference forecast (e.g., climatology).
    /// BSS = 1 - (BS / BS_ref). A BSS of 1 is perfect, 0 is no skill, negative is worse than reference.
    /// </summary>
    /// <param name="brierScore">The model's Brier score.</param>
    /// <param name="referenceBrierScore">The reference (baseline) Brier score.</param>
    /// <returns>The Brier skill score.</returns>
    public static double CalculateSkillScore(double brierScore, double referenceBrierScore)
    {
        if (referenceBrierScore <= 0.0)
            throw new DomainException(ErrorCodes.ValidationError,
                "Reference Brier score must be positive for skill score calculation.");

        return 1.0 - (brierScore / referenceBrierScore);
    }
}
