using System.ComponentModel.DataAnnotations;

namespace Taskdeck.Application.Services;

/// <summary>
/// Configuration for LLM quota enforcement.
/// Bound from the "LlmQuota" configuration section.
/// </summary>
public class LlmQuotaSettings
{
    /// <summary>Maximum requests per user per hour. 0 = unlimited.</summary>
    [Range(0, int.MaxValue, ErrorMessage = "RequestsPerHour must be non-negative.")]
    public int RequestsPerHour { get; set; } = 60;

    /// <summary>Maximum total tokens per user per day (input + output). 0 = unlimited.</summary>
    [Range(0L, long.MaxValue, ErrorMessage = "TokensPerDay must be non-negative.")]
    public long TokensPerDay { get; set; } = 100_000;

    /// <summary>
    /// Global budget ceiling in tokens per day across all users. 0 = unlimited.
    /// </summary>
    [Range(0L, long.MaxValue, ErrorMessage = "GlobalBudgetCeilingTokens must be non-negative.")]
    public long GlobalBudgetCeilingTokens { get; set; } = 0;
}
