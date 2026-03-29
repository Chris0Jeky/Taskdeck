namespace Taskdeck.Application.Services;

/// <summary>
/// Configuration for abuse detection thresholds.
/// Bound from the "AbuseDetection" configuration section.
/// </summary>
public class AbuseDetectionSettings
{
    /// <summary>Whether abuse detection is enabled. Defaults to true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Requests per user per hour that trigger AnomalousVelocity signal.
    /// Must be higher than the normal quota RequestsPerHour.
    /// 0 = disabled.
    /// </summary>
    public int VelocityRequestsPerHourThreshold { get; set; } = 120;

    /// <summary>
    /// Tokens per user per hour that trigger AnomalousVelocity signal.
    /// 0 = disabled.
    /// </summary>
    public long VelocityTokensPerHourThreshold { get; set; } = 200_000;

    /// <summary>
    /// Number of quota-denied requests in a sliding window that trigger LimitHitEvasion signal.
    /// 0 = disabled.
    /// </summary>
    public int LimitHitEvasionThreshold { get; set; } = 10;

    /// <summary>
    /// Number of blocked/refused content responses that trigger RepeatedBlockedContent signal.
    /// 0 = disabled.
    /// </summary>
    public int BlockedContentThreshold { get; set; } = 5;

    /// <summary>
    /// Number of accumulated signals before escalating from Observe to Suspicious.
    /// </summary>
    public int SuspiciousSignalThreshold { get; set; } = 3;

    /// <summary>
    /// Number of accumulated signals before escalating from Suspicious to Restricted.
    /// </summary>
    public int RestrictedSignalThreshold { get; set; } = 6;

    /// <summary>
    /// Number of accumulated signals before escalating from Restricted to Blocked.
    /// </summary>
    public int BlockedSignalThreshold { get; set; } = 10;

    /// <summary>
    /// Sliding window in minutes for signal accumulation evaluation.
    /// </summary>
    public int EvaluationWindowMinutes { get; set; } = 60;
}
