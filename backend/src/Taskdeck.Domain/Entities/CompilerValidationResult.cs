namespace Taskdeck.Domain.Entities;

/// <summary>
/// Result of compiling/validating a proposal's operations.
/// Immutable after creation.
/// </summary>
public sealed class CompilerValidationResult
{
    public bool IsValid { get; }
    public IReadOnlyList<OperationRisk> Risks { get; }
    public IReadOnlyList<UnsupportedOperationFailure> Failures { get; }

    private CompilerValidationResult(
        bool isValid,
        IReadOnlyList<OperationRisk> risks,
        IReadOnlyList<UnsupportedOperationFailure> failures)
    {
        IsValid = isValid;
        Risks = risks;
        Failures = failures;
    }

    /// <summary>
    /// Creates a successful validation result with optional risk warnings.
    /// </summary>
    public static CompilerValidationResult Success(IReadOnlyList<OperationRisk>? risks = null)
    {
        return new CompilerValidationResult(
            true,
            risks ?? Array.Empty<OperationRisk>(),
            Array.Empty<UnsupportedOperationFailure>());
    }

    /// <summary>
    /// Creates a failed validation result with failures and optional risk warnings.
    /// </summary>
    public static CompilerValidationResult Failure(
        IReadOnlyList<UnsupportedOperationFailure> failures,
        IReadOnlyList<OperationRisk>? risks = null)
    {
        if (failures == null || failures.Count == 0)
            throw new ArgumentException("Failures must not be empty for a failed result.", nameof(failures));

        return new CompilerValidationResult(
            false,
            risks ?? Array.Empty<OperationRisk>(),
            failures);
    }

    /// <summary>
    /// The aggregate risk level: the highest risk among all assessed risks,
    /// or Low if no risks are present.
    /// </summary>
    public RiskLevel AggregateRiskLevel
    {
        get
        {
            if (Risks.Count == 0) return RiskLevel.Low;
            return Risks.Max(r => r.Level);
        }
    }
}
