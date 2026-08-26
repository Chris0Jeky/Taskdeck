namespace Taskdeck.Domain.Enums;

/// <summary>
/// The latest explicit routing choice made for a persisted capture.
/// This is separate from queue processing status: keeping a capture does not queue work,
/// while requesting a proposal still requires review and explicit execution.
/// </summary>
public enum CaptureDisposition
{
    Kept = 0,
    Archived = 1,
    ProposalRequested = 2
}
