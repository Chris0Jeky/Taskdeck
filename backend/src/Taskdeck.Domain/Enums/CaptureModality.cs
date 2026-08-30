namespace Taskdeck.Domain.Enums;

/// <summary>
/// What kind of source material a capture primarily carries (ADR-0065 §Decision 2). One of the
/// four independent capture dimensions that replace the overloaded <see cref="CaptureSource"/>
/// as the canonical model; <see cref="CaptureSource"/> survives only as a derived compatibility
/// field (<see cref="CaptureSourceMapping"/>).
/// </summary>
public enum CaptureModality
{
    Text = 0,
    Audio = 1,
    Image = 2,
    Document = 3,
    Structured = 4
}
