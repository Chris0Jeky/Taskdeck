using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Taskdeck.Api.Contracts;
using Taskdeck.Api.Dtos;
using Taskdeck.Api.Extensions;
using Taskdeck.Application.Interfaces;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/automation/metrics")]
[Produces("application/json")]
public class AutomationMetricsController : AuthenticatedControllerBase
{
    private const int MaxRangeDays = 365;

    public AutomationMetricsController(IUserContext userContext)
        : base(userContext)
    {
    }

    [HttpGet("cohorts")]
    [ProducesResponseType(typeof(CohortComparisonResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public IActionResult GetCohortMetrics(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to)
    {
        if (!TryGetCurrentUserId(out _, out var errorResult))
            return errorResult!;

        var fromDate = from ?? DateTime.UtcNow.AddDays(-30);
        var toDate = to ?? DateTime.UtcNow;

        if (fromDate >= toDate)
            return BadRequest(new ApiErrorResponse(
                ErrorCodes.ValidationError,
                "'from' must be earlier than 'to'."));

        if ((toDate - fromDate).TotalDays > MaxRangeDays)
            return BadRequest(new ApiErrorResponse(
                ErrorCodes.ValidationError,
                $"Date range must not exceed {MaxRangeDays} days."));

        // TODO(RFAI-12): wire to ICohortMetricsService once learning-loop data layer ships
        var response = new CohortComparisonResponse
        {
            Cohorts = [],
            DateRange = new DateRangeDto { From = fromDate.ToString("O"), To = toDate.ToString("O") }
        };

        return Ok(response);
    }
}
