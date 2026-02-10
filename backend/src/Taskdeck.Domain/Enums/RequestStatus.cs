namespace Taskdeck.Domain.Enums;

/// <summary>
/// Defines the status of an LLM request in the queue.
/// </summary>
public enum RequestStatus
{
    /// <summary>
    /// Request is waiting to be processed.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Request is currently being processed.
    /// </summary>
    Processing = 1,

    /// <summary>
    /// Request has been successfully processed.
    /// </summary>
    Completed = 2,

    /// <summary>
    /// Request processing failed.
    /// </summary>
    Failed = 3,

    /// <summary>
    /// Request was cancelled by the user.
    /// </summary>
    Cancelled = 4
}
