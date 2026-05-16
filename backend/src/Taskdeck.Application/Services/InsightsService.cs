using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Services;

public sealed class InsightsService : IInsightsService
{
    private readonly IProposalOutcomeRepository _outcomeRepository;

    private const int MaxPeriodDays = 365;
    private const int DefaultPeriodDays = 30;

    public InsightsService(IProposalOutcomeRepository outcomeRepository)
    {
        _outcomeRepository = outcomeRepository;
    }

    public async Task<InsightCohort> GetProposalCohortAsync(
        Guid userId,
        int periodDays,
        CancellationToken cancellationToken = default)
    {
        var bounded = BoundPeriod(periodDays);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-bounded);

        var outcomes = await _outcomeRepository.GetAllByUserIdAsync(userId, cancellationToken);

        var filtered = outcomes.Where(o => o.DecidedAt >= cutoff).ToList();

        var accepted = filtered.Count(o => o.Decision == OutcomeDecision.Approved);
        var edited = filtered.Count(o => o.Decision == OutcomeDecision.EditedThenApproved);
        var rejected = filtered.Count(o => o.Decision == OutcomeDecision.Rejected);

        return new InsightCohort(accepted, edited, rejected);
    }

    public async Task<IReadOnlyList<InsightMetric>> GetMetricsAsync(
        Guid userId,
        int periodDays,
        CancellationToken cancellationToken = default)
    {
        var cohort = await GetProposalCohortAsync(userId, periodDays, cancellationToken);
        var bounded = BoundPeriod(periodDays);

        var metrics = new List<InsightMetric>();

        if (cohort.TotalCount > 0)
        {
            var bucketedTotal = BucketCount(cohort.TotalCount);

            metrics.Add(new InsightMetric(
                "proposal.acceptance_rate",
                BucketCount(cohort.AcceptedCount),
                bounded,
                "v1.0"));

            metrics.Add(new InsightMetric(
                "proposal.edit_rate",
                BucketCount(cohort.EditedCount),
                bounded,
                "v1.0"));

            metrics.Add(new InsightMetric(
                "proposal.rejection_rate",
                BucketCount(cohort.RejectedCount),
                bounded,
                "v1.0"));

            metrics.Add(new InsightMetric(
                "proposal.generated_count",
                bucketedTotal,
                bounded,
                "v1.0"));
        }

        return metrics.AsReadOnly();
    }

    private static int BoundPeriod(int periodDays)
    {
        return periodDays <= 0 ? DefaultPeriodDays : Math.Min(periodDays, MaxPeriodDays);
    }

    private static int BucketCount(int count)
    {
        return count switch
        {
            0 => 0,
            <= 5 => 5,
            <= 10 => 10,
            <= 25 => 25,
            <= 50 => 50,
            <= 100 => 100,
            _ => (count / 50) * 50
        };
    }
}
