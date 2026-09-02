
namespace Taskdeck.Acceleration.V06;

public sealed record ProposalMetricFact(
    Guid ProposalId,
    bool Reviewed,
    bool Approved,
    bool ApprovedUnchanged,
    bool Rejected,
    bool? TargetCorrect,
    bool? PermissionCorrect,
    bool? FalseAction,
    bool? CorrectNoAction,
    int AcceptedOperationCount,
    decimal? AttributableCost,
    string? Currency);

public sealed record RateMetric(
    string Name,
    int Numerator,
    int Denominator,
    int Unknown,
    double? Value,
    bool MinimumCohortMet);

public sealed record CostMetric(
    decimal KnownCost,
    int AcceptedOperations,
    int UnknownCostRecords,
    decimal? CostPerAcceptedOperation,
    string? Currency);

public static class ContextFabricMetricCalculator
{
    public static RateMetric UnchangedAcceptance(
        IEnumerable<ProposalMetricFact> facts,
        int minimumCohort)
    {
        var reviewed = facts.Where(x => x.Reviewed).ToList();
        var numerator = reviewed.Count(x => x.ApprovedUnchanged);
        var denominator = reviewed.Count;
        return Rate(
            "unchanged-acceptance",
            numerator,
            denominator,
            unknown: 0,
            minimumCohort);
    }

    public static RateMetric TargetAccuracy(
        IEnumerable<ProposalMetricFact> facts,
        int minimumCohort)
    {
        var labeled = facts.Where(x => x.TargetCorrect.HasValue).ToList();
        return Rate(
            "target-accuracy",
            labeled.Count(x => x.TargetCorrect == true),
            labeled.Count,
            facts.Count() - labeled.Count,
            minimumCohort);
    }

    public static CostMetric CostPerAcceptedOperation(
        IEnumerable<ProposalMetricFact> facts)
    {
        var accepted = facts.Sum(x => x.AcceptedOperationCount);
        var known = facts.Where(x => x.AttributableCost.HasValue).ToList();
        var currencies = known.Select(x => x.Currency).Where(x => x is not null).Distinct().ToList();
        if (currencies.Count > 1)
            return new CostMetric(known.Sum(x => x.AttributableCost ?? 0), accepted,
                facts.Count() - known.Count, null, null);

        var total = known.Sum(x => x.AttributableCost ?? 0);
        return new CostMetric(
            total,
            accepted,
            facts.Count() - known.Count,
            accepted == 0 ? null : total / accepted,
            currencies.SingleOrDefault());
    }

    private static RateMetric Rate(
        string name, int numerator, int denominator, int unknown, int minimumCohort) =>
        new(
            name,
            numerator,
            denominator,
            unknown,
            denominator == 0 ? null : (double)numerator / denominator,
            denominator >= minimumCohort);
}
