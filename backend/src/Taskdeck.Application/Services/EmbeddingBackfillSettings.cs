using System.ComponentModel.DataAnnotations;

namespace Taskdeck.Application.Services;

/// <summary>
/// Configuration for the embedding backfill worker.
/// </summary>
public class EmbeddingBackfillSettings
{
    /// <summary>
    /// Whether the backfill worker is enabled. When false, no embedding
    /// backfill occurs even if vector dependencies are available.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Number of items to process in each backfill batch.
    /// </summary>
    [Range(1, 500, ErrorMessage = "BatchSize must be between 1 and 500.")]
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// Seconds to wait between backfill iterations.
    /// </summary>
    [Range(1, 86400, ErrorMessage = "PollIntervalSeconds must be between 1 and 86400.")]
    public int PollIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum number of consecutive errors before the worker pauses
    /// with exponential backoff.
    /// </summary>
    [Range(1, 100, ErrorMessage = "MaxConsecutiveErrors must be between 1 and 100.")]
    public int MaxConsecutiveErrors { get; set; } = 5;

    /// <summary>
    /// Maximum backoff delay in seconds when consecutive errors occur.
    /// </summary>
    [Range(10, 3600, ErrorMessage = "MaxBackoffSeconds must be between 10 and 3600.")]
    public int MaxBackoffSeconds { get; set; } = 300;
}
