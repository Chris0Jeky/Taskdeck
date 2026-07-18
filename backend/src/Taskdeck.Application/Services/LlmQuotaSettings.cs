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

    /// <summary>
    /// Tokens held per in-flight reservation (issue #1313) before the actual usage is known. Concurrent
    /// callers see each other's estimate against the token budget, bounding token overshoot at the
    /// boundary; the estimate is replaced by the real count when the reservation commits. Must exceed a
    /// typical single call's usage to be effective without being so large it starves normal traffic.
    /// Minimum 1: an estimate of 0 would make reserved rows invisible to the token/global-budget sums,
    /// silently reopening the concurrent token TOCTOU the reservation exists to close.
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "ReservationEstimatedTokens must be at least 1.")]
    public int ReservationEstimatedTokens { get; set; } = 2_000;

    /// <summary>
    /// How long a reservation stays live before it is treated as stale and swept. A crash between
    /// reserve and commit leaves an orphan row; after this many seconds it stops counting toward quota
    /// and is deleted on the next reservation attempt. Long enough to outlast a slow LLM call.
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "ReservationTtlSeconds must be positive.")]
    public int ReservationTtlSeconds { get; set; } = 120;
}
