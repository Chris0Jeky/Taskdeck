using System.ComponentModel.DataAnnotations;

namespace Taskdeck.Application.Services;

public class WorkerSettings
{
    [Range(1, 3600, ErrorMessage = "QueuePollIntervalSeconds must be between 1 and 3600.")]
    public int QueuePollIntervalSeconds { get; set; } = 5;

    [Range(1, 100, ErrorMessage = "MaxBatchSize must be between 1 and 100.")]
    public int MaxBatchSize { get; set; } = 5;

    [Range(1, 50, ErrorMessage = "MaxConcurrency must be between 1 and 50.")]
    public int MaxConcurrency { get; set; } = 2;

    [Range(0, 20, ErrorMessage = "MaxRetries must be between 0 and 20.")]
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Backoff durations in seconds for each retry attempt.
    /// Cross-property validation ensures Length >= MaxRetries (see WorkerSettingsValidator).
    /// </summary>
    [Required]
    [MinLength(1, ErrorMessage = "RetryBackoffSeconds must contain at least one entry.")]
    public int[] RetryBackoffSeconds { get; set; } = new[] { 10, 30, 90 };

    [Range(10, 7200, ErrorMessage = "ProcessingLeaseSeconds must be between 10 and 7200.")]
    public int ProcessingLeaseSeconds { get; set; } = 120;

    [Range(1, 525600, ErrorMessage = "ProposalExpiryMinutes must be between 1 and 525600 (1 year).")]
    public int ProposalExpiryMinutes { get; set; } = 1440;

    public bool EnableAutoQueueProcessing { get; set; } = true;
}
