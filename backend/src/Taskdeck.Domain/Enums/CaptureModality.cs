namespace Taskdeck.Domain.Enums;

/// <summary>
/// What kind of source material an asset carries (ADR-0065 §Decision 2). Each
/// <see cref="Entities.SourceAsset"/> has exactly one modality and routing operates per asset;
/// <c>Capture.PrimaryModality</c> is only a summary of the first or dominant asset for lists and
/// compatibility readers. One of the four independent capture dimensions that replace the
/// overloaded <see cref="CaptureSource"/> as the canonical model; <see cref="CaptureSource"/>
/// survives only as a compatibility snapshot (<see cref="CaptureSourceMapping"/>).
/// </summary>
public enum CaptureModality
{
    Text = 0,
    Audio = 1,
    Image = 2,
    Document = 3,
    Structured = 4
}
