namespace Taskdeck.Domain.Enums;

/// <summary>
/// One evidence vocabulary for every modality (ADR-0065 §Decision 4). Each kind permits exactly one
/// set of location fields; the shipped transcript char spans are <see cref="TextSpan"/> anchors.
/// </summary>
public enum EvidenceAnchorKind
{
    /// <summary>Half-open UTF-16 char range over a text or transcript representation.</summary>
    TextSpan = 0,

    /// <summary>Millisecond range over an audio-derived representation.</summary>
    TimeRange = 1,

    /// <summary>A normalised rectangle on a numbered page of a document representation.</summary>
    PageRegion = 2,

    /// <summary>A normalised rectangle on an image representation.</summary>
    ImageRegion = 3,

    /// <summary>A JSON pointer into a structured representation.</summary>
    JsonPointer = 4,

    /// <summary>The whole source, when no finer location is honest.</summary>
    WholeSource = 5
}
