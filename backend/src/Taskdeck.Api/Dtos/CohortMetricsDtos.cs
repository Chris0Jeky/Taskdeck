namespace Taskdeck.Api.Dtos;

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
