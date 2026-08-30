namespace Taskdeck.Domain.Enums;

/// <summary>
/// A projection of the planning and execution records attached to a capture (candidates, context
/// bindings, change sets, receipts — CF-08 / CF-09 / CF-21). A summary column, never the
/// authoritative record. Independent of <see cref="CaptureUserDisposition"/>: a capture that was
/// acted on stays <see cref="Acted"/> when the user later archives it, because the outcome remains
/// true.
/// </summary>
public enum CaptureActionState
{
    /// <summary>No change set has been planned against project state.</summary>
    Unplanned = 0,

    /// <summary>Planning stopped on one narrow question for the user (an unresolved target, a missing choice) — never a failed job (ADR-0065 §Decision 12).</summary>
    NeedsInput = 1,

    /// <summary>A change set awaits an explicit human decision (review-first, ADR-0003 / GP-06).</summary>
    NeedsReview = 2,

    /// <summary>An authorised change set was executed and a receipt exists.</summary>
    Acted = 3
}
