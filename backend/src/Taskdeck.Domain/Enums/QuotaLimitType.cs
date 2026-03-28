namespace Taskdeck.Domain.Enums;

/// <summary>
/// Distinguishes between advisory (soft) and enforced (hard) quota limits.
/// </summary>
public enum QuotaLimitType
{
    Soft = 0,
    Hard = 1
}
