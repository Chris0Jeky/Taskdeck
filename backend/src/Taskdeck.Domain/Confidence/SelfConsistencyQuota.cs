using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Confidence;

/// <summary>
/// Tracks the budget for self-consistency checks (multiple LLM calls for variance detection).
/// Immutable — mutations return a new instance.
/// </summary>
public sealed class SelfConsistencyQuota : IEquatable<SelfConsistencyQuota>
{
    /// <summary>
    /// Epsilon used for floating-point tolerance in cost comparisons,
    /// consistent with the epsilon used in Equals.
    /// </summary>
    private const double Epsilon = 1e-12;

    /// <summary>
    /// Maximum number of self-consistency calls allowed in this budget window.
    /// </summary>
    public int MaxCalls { get; }

    /// <summary>
    /// Number of calls consumed so far.
    /// </summary>
    public int UsedCalls { get; }

    /// <summary>
    /// Optional cost cap in abstract cost units. Null means no cost cap.
    /// </summary>
    public double? CostCap { get; }

    /// <summary>
    /// Cost consumed so far.
    /// </summary>
    public double CostUsed { get; }

    /// <summary>
    /// Remaining calls available.
    /// </summary>
    public int RemainingCalls => MaxCalls - UsedCalls;

    /// <summary>
    /// Whether the budget has remaining capacity.
    /// </summary>
    public bool HasBudget => RemainingCalls > 0 && (CostCap is null || CostUsed < CostCap.Value);

    public SelfConsistencyQuota(int maxCalls, int usedCalls = 0, double? costCap = null, double costUsed = 0.0)
    {
        if (maxCalls < 0)
            throw new DomainException(ErrorCodes.ValidationError,
                "MaxCalls cannot be negative.");

        if (usedCalls < 0)
            throw new DomainException(ErrorCodes.ValidationError,
                "UsedCalls cannot be negative.");

        if (usedCalls > maxCalls)
            throw new DomainException(ErrorCodes.ValidationError,
                $"UsedCalls ({usedCalls}) cannot exceed MaxCalls ({maxCalls}).");

        if (costCap.HasValue && (double.IsNaN(costCap.Value) || double.IsInfinity(costCap.Value)))
            throw new DomainException(ErrorCodes.ValidationError,
                "CostCap must be a finite number.");

        if (costCap.HasValue && costCap.Value < 0)
            throw new DomainException(ErrorCodes.ValidationError,
                "CostCap cannot be negative.");

        if (double.IsNaN(costUsed) || double.IsInfinity(costUsed))
            throw new DomainException(ErrorCodes.ValidationError,
                "CostUsed must be a finite number.");

        if (costUsed < 0)
            throw new DomainException(ErrorCodes.ValidationError,
                "CostUsed cannot be negative.");

        if (costCap.HasValue && costUsed > costCap.Value + Epsilon)
            throw new DomainException(ErrorCodes.ValidationError,
                $"CostUsed ({costUsed}) cannot exceed CostCap ({costCap.Value}).");

        MaxCalls = maxCalls;
        UsedCalls = usedCalls;
        CostCap = costCap;
        CostUsed = costUsed;
    }

    /// <summary>
    /// Records consumption of one call with the given cost, returning a new quota.
    /// Throws if the budget is exhausted.
    /// </summary>
    public SelfConsistencyQuota Consume(double callCost = 0.0)
    {
        if (double.IsNaN(callCost) || double.IsInfinity(callCost))
            throw new DomainException(ErrorCodes.ValidationError,
                "Call cost must be a finite number.");

        if (callCost < 0)
            throw new DomainException(ErrorCodes.ValidationError,
                "Call cost cannot be negative.");

        if (RemainingCalls <= 0)
            throw new DomainException(ErrorCodes.LlmQuotaExceeded,
                "Self-consistency call budget exhausted.");

        var newCostUsed = CostUsed + callCost;

        if (CostCap.HasValue && newCostUsed > CostCap.Value + Epsilon)
            throw new DomainException(ErrorCodes.LlmQuotaExceeded,
                $"Self-consistency cost cap would be exceeded ({newCostUsed:F4} > {CostCap.Value:F4}).");

        return new SelfConsistencyQuota(MaxCalls, UsedCalls + 1, CostCap, newCostUsed);
    }

    #region Equality

    public bool Equals(SelfConsistencyQuota? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return MaxCalls == other.MaxCalls
               && UsedCalls == other.UsedCalls
               && CostCap == other.CostCap
               && Math.Abs(CostUsed - other.CostUsed) < Epsilon;
    }

    public override bool Equals(object? obj) => Equals(obj as SelfConsistencyQuota);

    public override int GetHashCode()
    {
        // Round CostUsed to a granularity coarser than the epsilon (1e-12) used in Equals
        // so that two values within epsilon produce the same hash code.
        long roundedCostBits = (long)Math.Round(CostUsed * 1e9);
        return HashCode.Combine(MaxCalls, UsedCalls, CostCap, roundedCostBits);
    }

    #endregion

    public override string ToString() =>
        $"Quota: {UsedCalls}/{MaxCalls} calls, cost {CostUsed:F4}" +
        (CostCap.HasValue ? $"/{CostCap.Value:F4}" : " (uncapped)");
}
