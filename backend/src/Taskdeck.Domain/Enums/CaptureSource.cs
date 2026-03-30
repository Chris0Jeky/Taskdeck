namespace Taskdeck.Domain.Enums;

/// <summary>
/// Describes where a capture artifact originated.
/// </summary>
public enum CaptureSource
{
    Typed = 0,
    Paste = 1,
    TranscriptPaste = 2,
    Import = 3,
    Voice = 4,
    MeetingIntegration = 5,
    TranscriptFile = 6
}
