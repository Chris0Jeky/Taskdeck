namespace Taskdeck.Domain.Enums;

/// <summary>
/// A projection of the processing jobs and runs attached to a capture (ADR-0065 §Decision 6;
/// CF-03). It is a summary column, never the authoritative record: the job and run tables own the
/// truth once CF-03 lands, and the runner rewrites this value from them. Multi-asset captures are
/// why the axis exists — one failed image processor must not turn a text-plus-screenshot capture
/// into a global failure when the text path succeeded (<see cref="Partial"/>).
/// </summary>
public enum CaptureProcessingSummary
{
    /// <summary>No job has been requested (a <see cref="CaptureIntentMode.Remember"/> capture, or nothing to derive).</summary>
    Idle = 0,

    /// <summary>At least one job is pending, leased or running.</summary>
    Processing = 1,

    /// <summary>Some source assets have usable representations and at least one job failed or is still outstanding.</summary>
    Partial = 2,

    /// <summary>Every requested representation exists.</summary>
    Ready = 3,

    /// <summary>Every attempted job failed; the sources remain readable and retryable.</summary>
    Failed = 4
}
