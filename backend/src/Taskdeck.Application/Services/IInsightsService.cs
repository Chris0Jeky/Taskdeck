using Taskdeck.Application.Services;

namespace Taskdeck.Application.Services;

public interface IInsightsService
{
    Task<InsightCohort> GetProposalCohortAsync(Guid userId, int periodDays, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InsightMetric>> GetMetricsAsync(Guid userId, int periodDays, CancellationToken cancellationToken = default);
}
