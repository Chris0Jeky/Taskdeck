namespace Taskdeck.Domain.Enums;

/// <summary>
/// State of a <c>ProcessingJob</c> (ADR-0065 §Decision 6, CF-03). Job state is machinery: it never
/// leaks into the capture's <see cref="CaptureProcessingSummary"/> projection except through an
/// explicit, user-legible transition (the three capture state axes replaced the lifecycle enum on 2026-08-30).
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
