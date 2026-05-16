using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.Interfaces;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/automation/metrics")]
[Produces("application/json")]
public class AutomationMetricsController : AuthenticatedControllerBase
{
    public AutomationMetricsController(IUserContext userContext)
        : base(userContext)
    {
    }

    [HttpGet("cohorts")]
    [ProducesResponseType(typeof(CohortComparisonResponse), StatusCodes.Status200OK)]
    public IActionResult GetCohortMetrics(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to)
    {
        var fromDate = from ?? DateTime.UtcNow.AddDays(-30);
        var toDate = to ?? DateTime.UtcNow;

        var response = new CohortComparisonResponse
        {
            Cohorts = [],
            DateRange = new DateRangeDto { From = fromDate.ToString("O"), To = toDate.ToString("O") }
        };

        return Ok(response);
    }
}

public sealed class CohortComparisonResponse
{
    public required List<CohortMetricsDto> Cohorts { get; set; }
    public required DateRangeDto DateRange { get; set; }
}

public sealed class CohortMetricsDto
{
    public required string CohortId { get; set; }
    public required string PromptVersion { get; set; }
    public int TotalProposals { get; set; }
    public int Accepted { get; set; }
    public int Edited { get; set; }
    public int Rejected { get; set; }
    public long AverageTimeToDecisionMs { get; set; }
}

public sealed class DateRangeDto
{
    public required string From { get; set; }
    public required string To { get; set; }
}
