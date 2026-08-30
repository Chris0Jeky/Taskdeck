namespace Taskdeck.Domain.Enums;

/// <summary>
/// The kind of an immutable derived view of a source (ADR-0065 §Decision 3). The shipped
/// <see cref="Entities.Transcript"/> is the <see cref="Transcript"/> payload and the shipped
/// <see cref="Entities.ArtefactExtraction"/> is the <see cref="NormalizedText"/> payload behind the
/// representation façade (CF-06).
/// </summary>
public enum RepresentationKind
{
    NormalizedText = 0,
    Transcript = 1,
    OcrText = 2,
    ImageDescription = 3,
    DocumentStructure = 4,
    StructuredEvent = 5
}
