using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/insights")]
[Produces("application/json")]
public class InsightsController : AuthenticatedControllerBase
{
    private readonly IInsightsService _insightsService;

    public InsightsController(IInsightsService insightsService, IUserContext userContext)
        : base(userContext)
    {
        _insightsService = insightsService;
    }

    [HttpGet("cohort")]
    [ProducesResponseType(typeof(InsightCohortResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetCohort([FromQuery] int periodDays = 30, CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var cohort = await _insightsService.GetProposalCohortAsync(userId, periodDays, cancellationToken);

        return Ok(new InsightCohortResponse(
            cohort.AcceptedCount,
            cohort.EditedCount,
            cohort.RejectedCount,
            cohort.TotalCount,
            cohort.AcceptanceRate));
    }

    [HttpGet("metrics")]
    [ProducesResponseType(typeof(InsightMetricsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMetrics([FromQuery] int periodDays = 30, CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId, out var errorResult))
            return errorResult!;

        var metrics = await _insightsService.GetMetricsAsync(userId, periodDays, cancellationToken);

        var dtos = metrics.Select(m => new InsightMetricDto(
            m.MetricName,
            m.BucketedCount,
            m.TimePeriodDays,
            m.PromptVersion)).ToList();

        return Ok(new InsightMetricsResponse(dtos));
    }
}

public sealed record InsightCohortResponse(
    int AcceptedCount,
    int EditedCount,
    int RejectedCount,
    int TotalCount,
    double AcceptanceRate);

public sealed record InsightMetricsResponse(
    IReadOnlyList<InsightMetricDto> Metrics);

public sealed record InsightMetricDto(
    string MetricName,
    int BucketedCount,
    int TimePeriodDays,
    string PromptVersion);
