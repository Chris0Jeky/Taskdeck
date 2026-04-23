using System.ComponentModel.DataAnnotations;

namespace Taskdeck.Application.Services;

/// <summary>
/// Configuration for the audit log retention policy.
/// Controls how long audit entries are kept and how cleanup is performed.
/// </summary>
public class AuditRetentionSettings
{
    /// <summary>
    /// Maximum number of days to retain audit log entries.
    /// Entries older than this are eligible for cleanup.
    /// </summary>
    [Range(1, 3650, ErrorMessage = "MaxRetentionDays must be between 1 and 3650 (10 years).")]
    public int MaxRetentionDays { get; set; } = 90;

    /// <summary>
    /// Number of rows to delete per batch during cleanup.
    /// Smaller batches reduce lock contention but take more iterations.
    /// </summary>
    [Range(1, 50000, ErrorMessage = "CleanupBatchSize must be between 1 and 50000.")]
    public int CleanupBatchSize { get; set; } = 1000;

    /// <summary>
    /// Interval in hours between cleanup runs.
    /// </summary>
    [Range(1, 720, ErrorMessage = "CleanupIntervalHours must be between 1 and 720 (30 days).")]
    public int CleanupIntervalHours { get; set; } = 24;
}
