namespace Taskdeck.Domain.Enums;

/// <summary>
/// The user-legible lifecycle of a durable <see cref="Entities.Capture"/> (ADR-0065 §Decision 1;
/// the timeline CF-20 renders). It is deliberately independent of processing-job state: a job can
/// fail and be retried without the capture ever leaving a readable state.
/// </summary>
public enum CaptureLifecycleState
{
    /// <summary>Source material is durably stored. This is the only state a capture needs to be safe.</summary>
    Received = 0,

    /// <summary>Representations are being derived (transcription, OCR, normalisation).</summary>
    Preparing = 1,

    /// <summary>Semantic candidates exist; nothing has been planned against project state yet.</summary>
    Understood = 2,

    /// <summary>A target (board today, project later) has been resolved for the candidates.</summary>
    Routed = 3,

    /// <summary>A change set awaits an explicit human decision.</summary>
    NeedsReview = 4,

    /// <summary>The authorised change set was executed and a receipt exists.</summary>
    Acted = 5,

    /// <summary>Kept as a record on purpose (the <see cref="CaptureIntentMode.Remember"/> outcome).</summary>
    Kept = 6,

    /// <summary>Every processing attempt failed; the source is still readable and retryable.</summary>
    Failed = 7,

    /// <summary>Archived by the user; terminal.</summary>
    Archived = 8
}

/// <summary>
/// The allowed <see cref="CaptureLifecycleState"/> transitions. Kept beside the enum, like
/// <see cref="CaptureStatusPolicy"/>, so the two lifecycles can be compared line by line.
/// </summary>
public static class CaptureLifecyclePolicy
{
    private static readonly IReadOnlyDictionary<CaptureLifecycleState, HashSet<CaptureLifecycleState>> AllowedTransitions =
        new Dictionary<CaptureLifecycleState, HashSet<CaptureLifecycleState>>
        {
            [CaptureLifecycleState.Received] = new() { CaptureLifecycleState.Preparing, CaptureLifecycleState.Kept, CaptureLifecycleState.Failed, CaptureLifecycleState.Archived },
            [CaptureLifecycleState.Preparing] = new() { CaptureLifecycleState.Understood, CaptureLifecycleState.Kept, CaptureLifecycleState.Failed },
            [CaptureLifecycleState.Understood] = new() { CaptureLifecycleState.Routed, CaptureLifecycleState.NeedsReview, CaptureLifecycleState.Kept, CaptureLifecycleState.Failed },
            [CaptureLifecycleState.Routed] = new() { CaptureLifecycleState.NeedsReview, CaptureLifecycleState.Acted, CaptureLifecycleState.Kept, CaptureLifecycleState.Failed },
            [CaptureLifecycleState.NeedsReview] = new() { CaptureLifecycleState.Acted, CaptureLifecycleState.Kept, CaptureLifecycleState.Preparing, CaptureLifecycleState.Archived },
            [CaptureLifecycleState.Acted] = new() { CaptureLifecycleState.Archived },
            [CaptureLifecycleState.Kept] = new() { CaptureLifecycleState.Preparing, CaptureLifecycleState.Archived },
            [CaptureLifecycleState.Failed] = new() { CaptureLifecycleState.Preparing, CaptureLifecycleState.Kept, CaptureLifecycleState.Archived },
            [CaptureLifecycleState.Archived] = new()
        };

    public static bool CanTransition(CaptureLifecycleState from, CaptureLifecycleState to)
    {
        if (from == to)
        {
            return true;
        }

        return AllowedTransitions.TryGetValue(from, out var allowed) && allowed.Contains(to);
    }

    public static bool IsTerminal(CaptureLifecycleState state) =>
        AllowedTransitions.TryGetValue(state, out var allowed) && allowed.Count == 0;
}
