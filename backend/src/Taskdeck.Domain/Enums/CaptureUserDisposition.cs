namespace Taskdeck.Domain.Enums;

/// <summary>
/// What the user has decided about a durable <see cref="Entities.Capture"/> — the only capture state
/// axis a person sets directly (ADR-0065 §Decision 1, amended 2026-08-30). Orthogonal to
/// <see cref="CaptureProcessingSummary"/> (what the machinery is doing) and
/// <see cref="CaptureActionState"/> (what has been planned or applied): a capture can be kept *and*
/// fully transcribed *and* already acted on. The legacy <see cref="CaptureDisposition"/> of the
/// queue-row model maps onto this axis through <see cref="CaptureUserDispositionMapping"/>.
/// </summary>
public enum CaptureUserDisposition
{
    /// <summary>In the Inbox; processing and planning may proceed under the requested intent.</summary>
    Active = 0,

    /// <summary>Preserved on purpose as a record (the <see cref="CaptureIntentMode.Remember"/> outcome). Still readable and re-processable.</summary>
    Kept = 1,

    /// <summary>Put away by the user; terminal. Processing and planning stop, existing outcomes stay true.</summary>
    Archived = 2
}

/// <summary>
/// Bridges the shipped queue-row disposition vocabulary to the durable axis. Total over
/// <see cref="CaptureDisposition"/>; the test suite enumerates the enum.
/// </summary>
public static class CaptureUserDispositionMapping
{
    public static CaptureUserDisposition FromLegacy(CaptureDisposition legacy) => legacy switch
    {
        CaptureDisposition.Kept => CaptureUserDisposition.Kept,
        CaptureDisposition.Archived => CaptureUserDisposition.Archived,
        CaptureDisposition.ProposalRequested => CaptureUserDisposition.Active,
        _ => throw new ArgumentOutOfRangeException(nameof(legacy), legacy, "Legacy capture disposition has no durable mapping")
    };
}
