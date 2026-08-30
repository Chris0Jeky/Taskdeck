namespace Taskdeck.Domain.Enums;

/// <summary>
/// State of a <c>ProcessingJob</c> (ADR-0065 §Decision 6, CF-03). Job state is machinery: it never
/// leaks into <see cref="CaptureLifecycleState"/> except through an explicit, user-legible transition.
/// </summary>
public enum ProcessingJobState
{
    Pending = 0,
    Leased = 1,
    Running = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5,
    Expired = 6
}
