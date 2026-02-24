namespace Taskdeck.Application.Services;

public class WorkerSettings
{
    public int QueuePollIntervalSeconds { get; set; } = 5;
    public int MaxBatchSize { get; set; } = 5;
    public int MaxConcurrency { get; set; } = 2;
    public int MaxRetries { get; set; } = 3;
    public int[] RetryBackoffSeconds { get; set; } = new[] { 10, 30, 90 };
    public int ProcessingLeaseSeconds { get; set; } = 120;
    public int ProposalExpiryMinutes { get; set; } = 1440;
    public bool EnableAutoQueueProcessing { get; set; } = true;
}
