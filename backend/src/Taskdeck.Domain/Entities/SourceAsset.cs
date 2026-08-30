using System.Security.Cryptography;
using System.Text;
using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// One immutable input of a <see cref="Capture"/> — the general source model between
/// <c>Capture</c> and <c>Representation</c> (ADR-0065 §Decision 1, amended 2026-08-30). Typed text,
/// pasted text, a URL plus an instruction, a screenshot, a voice note, a structured integration
/// event: each is one asset with its own modality, media type, content hash and storage kind, so a
/// capture may hold several (a sentence plus a screenshot) and routing runs per asset. Nothing here
/// is ever mutated after intake; the raw material a user gave Taskdeck stays exactly what they gave.
/// Typed and pasted text is an asset too (<see cref="FromInlineText"/>): it does not live on the
/// processing job, and it is not the capture's user note.
/// </summary>
public sealed class SourceAsset : Entity
{
    public const int MaxMediaTypeLength = 100;
    public const int MaxOriginalNameLength = 255;
    public const int MaxExternalReferenceLength = 2_048;
    public const int Sha256HexLength = 64;

    /// <summary>
    /// The largest text a single asset may carry inline — equal to the shipped transcript cap
    /// (<see cref="Transcript.MaxTextLength"/>), the largest text Taskdeck accepts today, so no
    /// capture the legacy contract accepts can fail to mirror.
    /// </summary>
    public const int MaxInlineTextLength = Transcript.MaxTextLength;

    public const string PlainTextMediaType = "text/plain";
    public const string UriListMediaType = "text/uri-list";

    public Guid CaptureId { get; private set; }

    /// <summary>Position within the capture, from zero; the first asset decides the capture's primary modality.</summary>
    public int Ordinal { get; private set; }

    public CaptureModality Modality { get; private set; }

    /// <summary>
    /// The media type declared for this asset. It belongs to the asset, not to the stored bytes:
    /// identical bytes may arrive with different declared media types (a <c>.txt</c> pasted as
    /// markdown), which is why a blob object carries no media type of its own.
    /// </summary>
    public string MediaType { get; private set; } = string.Empty;

    /// <summary>Lower-case hexadecimal SHA-256 over the asset's bytes (UTF-8 for inline text).</summary>
    public string ContentHash { get; private set; } = string.Empty;

    public long ByteSize { get; private set; }

    public SourceAssetStorageKind StorageKind { get; private set; }

    /// <summary>The <c>IBlobStore</c> reference that holds the bytes (CF-23); set only for <see cref="SourceAssetStorageKind.Blob"/>.</summary>
    public Guid? BlobReferenceId { get; private set; }

    /// <summary>
    /// Soft reference to the shipped <see cref="SourceArtefact"/> whose <see cref="ArtefactBlob"/>
    /// holds the bytes; set only for <see cref="SourceAssetStorageKind.LegacyArtefact"/>. No FK: the
    /// artefact keeps its own retention (ADR-0046) until CF-23 moves it behind the blob store.
    /// </summary>
    public Guid? LegacyArtefactId { get; private set; }

    /// <summary>A locator the user supplied (a URL, a share-target reference); never dereferenced at intake.</summary>
    public string? ExternalReference { get; private set; }

    /// <summary>The name the input arrived with (a file name, a page title); provenance only.</summary>
    public string? OriginalName { get; private set; }

    public SourceAssetTextPayload? TextPayload { get; private set; }

    private SourceAsset() : base()
    {
    }

    private SourceAsset(
        Guid captureId,
        int ordinal,
        CaptureModality modality,
        string mediaType,
        string contentHash,
        long byteSize,
        SourceAssetStorageKind storageKind,
        string? originalName)
        : base()
    {
        if (captureId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Capture ID cannot be empty");
        if (ordinal < 0)
            throw new DomainException(ErrorCodes.ValidationError, "Source asset ordinal cannot be negative");
        if (!Enum.IsDefined(modality))
            throw new DomainException(ErrorCodes.ValidationError, "Source asset modality is invalid");
        if (!Enum.IsDefined(storageKind))
            throw new DomainException(ErrorCodes.ValidationError, "Source asset storage kind is invalid");
        if (string.IsNullOrWhiteSpace(mediaType) || mediaType.Length > MaxMediaTypeLength)
            throw new DomainException(ErrorCodes.ValidationError, $"Media type is required and cannot exceed {MaxMediaTypeLength} characters");
        if (contentHash.Length != Sha256HexLength || contentHash.Any(character => !Uri.IsHexDigit(character)))
            throw new DomainException(ErrorCodes.ValidationError, "Content hash must be a 64-character hexadecimal SHA-256 digest");
        if (byteSize <= 0)
            throw new DomainException(ErrorCodes.ValidationError, "Source asset byte size must be greater than zero");
        if (originalName is not null && originalName.Length > MaxOriginalNameLength)
            throw new DomainException(ErrorCodes.ValidationError, $"Original name cannot exceed {MaxOriginalNameLength} characters");

        CaptureId = captureId;
        Ordinal = ordinal;
        Modality = modality;
        MediaType = mediaType.Trim();
        ContentHash = contentHash.ToLowerInvariant();
        ByteSize = byteSize;
        StorageKind = storageKind;
        OriginalName = string.IsNullOrWhiteSpace(originalName) ? null : originalName.Trim();
    }

    /// <summary>
    /// Typed or pasted text as an immutable asset. The text is stored verbatim (no normalisation —
    /// derived normalised text is a representation, not a source); the hash and size are computed
    /// over its UTF-8 encoding.
    /// </summary>
    public static SourceAsset FromInlineText(
        Guid captureId,
        int ordinal,
        string text,
        string mediaType = PlainTextMediaType,
        string? originalName = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new DomainException(ErrorCodes.ValidationError, "Source asset text cannot be empty");
        if (text.Length > MaxInlineTextLength)
            throw new DomainException(ErrorCodes.ValidationError, $"Source asset text cannot exceed {MaxInlineTextLength} characters");

        var bytes = Encoding.UTF8.GetBytes(text);
        var asset = new SourceAsset(
            captureId,
            ordinal,
            CaptureModality.Text,
            mediaType,
            HashOf(bytes),
            bytes.LongLength,
            SourceAssetStorageKind.InlineText,
            originalName);
        asset.TextPayload = new SourceAssetTextPayload(asset.Id, text);
        return asset;
    }

    /// <summary>A URL or other locator the user handed over, with no fetch at intake.</summary>
    public static SourceAsset FromExternalReference(
        Guid captureId,
        int ordinal,
        string reference,
        string? originalName = null)
    {
        if (string.IsNullOrWhiteSpace(reference))
            throw new DomainException(ErrorCodes.ValidationError, "External reference cannot be empty");
        var trimmed = reference.Trim();
        if (trimmed.Length > MaxExternalReferenceLength)
            throw new DomainException(ErrorCodes.ValidationError, $"External reference cannot exceed {MaxExternalReferenceLength} characters");

        var bytes = Encoding.UTF8.GetBytes(trimmed);
        var asset = new SourceAsset(
            captureId,
            ordinal,
            CaptureModality.Text,
            UriListMediaType,
            HashOf(bytes),
            bytes.LongLength,
            SourceAssetStorageKind.ExternalReference,
            originalName);
        asset.ExternalReference = trimmed;
        return asset;
    }

    /// <summary>Bytes held by <c>IBlobStore</c> under an acquired reference (CF-23 / CF-12).</summary>
    public static SourceAsset FromBlobReference(
        Guid captureId,
        int ordinal,
        CaptureModality modality,
        string mediaType,
        string contentHash,
        long byteSize,
        Guid blobReferenceId,
        string? originalName = null)
    {
        if (blobReferenceId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Blob reference ID cannot be empty");

        var asset = new SourceAsset(captureId, ordinal, modality, mediaType, contentHash, byteSize, SourceAssetStorageKind.Blob, originalName);
        asset.BlobReferenceId = blobReferenceId;
        return asset;
    }

    /// <summary>
    /// The shipped artefact pair adapted behind the source model: the asset mirrors the artefact's
    /// hash, size and MIME type and points at it softly, so nothing is copied or moved.
    /// </summary>
    public static SourceAsset FromLegacyArtefact(
        Guid captureId,
        int ordinal,
        CaptureModality modality,
        string mediaType,
        string sha256,
        long byteSize,
        Guid legacyArtefactId,
        string? originalName = null)
    {
        if (legacyArtefactId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Legacy artefact ID cannot be empty");

        var asset = new SourceAsset(captureId, ordinal, modality, mediaType, sha256, byteSize, SourceAssetStorageKind.LegacyArtefact, originalName);
        asset.LegacyArtefactId = legacyArtefactId;
        return asset;
    }

    public static string HashOf(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
