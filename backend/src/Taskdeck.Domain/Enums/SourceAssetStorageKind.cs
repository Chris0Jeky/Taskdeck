namespace Taskdeck.Domain.Enums;

/// <summary>
/// Where the immutable bytes of a <see cref="Entities.SourceAsset"/> live (ADR-0065 §Decision 1,
/// amended 2026-08-30: the general source model between <c>Capture</c> and <c>Representation</c>).
/// </summary>
public enum SourceAssetStorageKind
{
    /// <summary>Typed or pasted text, stored beside the asset as a <see cref="Entities.SourceAssetTextPayload"/>.</summary>
    InlineText = 0,

    /// <summary>Binary content held by <c>IBlobStore</c> under a blob reference (CF-23).</summary>
    Blob = 1,

    /// <summary>A URL or locator plus the user's instruction; Taskdeck stores the reference and never fetches it at intake.</summary>
    ExternalReference = 2,

    /// <summary>The bytes are the shipped <see cref="Entities.SourceArtefact"/> / <see cref="Entities.ArtefactBlob"/> pair, adapted behind this model until CF-23 moves them.</summary>
    LegacyArtefact = 3
}
