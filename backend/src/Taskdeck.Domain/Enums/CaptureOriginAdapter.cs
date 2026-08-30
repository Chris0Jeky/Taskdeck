namespace Taskdeck.Domain.Enums;

/// <summary>
/// The adapter or transport through which a capture entered Taskdeck (ADR-0065 §Decision 2).
/// Origin is provenance, not routing: processing is selected by capability and profile, never by
/// where the bytes came from.
/// </summary>
public enum CaptureOriginAdapter
{
    WebComposer = 0,
    FileUpload = 1,
    ShareTarget = 2,
    BrowserExtension = 3,
    VsCodeExtension = 4,
    Mcp = 5,
    Import = 6,
    Integration = 7,
    Api = 8
}
