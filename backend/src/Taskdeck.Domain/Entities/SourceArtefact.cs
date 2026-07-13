using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// Immutable metadata for a user-owned source artefact. Binary content is kept in
/// <see cref="ArtefactBlob"/> so metadata reads never materialize the blob column.
/// </summary>
public sealed class SourceArtefact : Entity
{
    public const int MaxFileNameLength = 255;
    public const int MaxMimeTypeLength = 100;
    public const int Sha256HexLength = 64;
    public const int MaxOriginReferenceLength = 1000;

    public Guid UserId { get; private set; }
    public Guid? BoardId { get; private set; }
    public ArtefactKind Kind { get; private set; }
    public string MimeType { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public long ByteSize { get; private set; }
    public string Sha256 { get; private set; } = string.Empty;
    public CaptureSource CaptureSource { get; private set; }

    /// <summary>
    /// Optional content-free source locator supplied by a trusted intake adapter.
    /// It is provenance only; Taskdeck never dereferences it during upload.
    /// </summary>
    public string? OriginReference { get; private set; }

    /// <summary>
    /// Optional link to an existing capture record. This is intentionally a soft
    /// reference because capture retention may be shorter than source retention.
    /// </summary>
    public Guid? CreatedFromCaptureId { get; private set; }

    private SourceArtefact() : base()
    {
    }

    public SourceArtefact(
        Guid userId,
        ArtefactKind kind,
        string mimeType,
        string fileName,
        long byteSize,
        string sha256,
        CaptureSource captureSource,
        Guid? boardId = null,
        string? originReference = null,
        Guid? createdFromCaptureId = null)
        : base()
    {
        if (userId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "User ID cannot be empty");
        if (boardId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Board ID cannot be empty");
        if (createdFromCaptureId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Capture ID cannot be empty");
        if (!Enum.IsDefined(kind))
            throw new DomainException(ErrorCodes.ValidationError, "Artefact kind is invalid");
        if (!Enum.IsDefined(captureSource))
            throw new DomainException(ErrorCodes.ValidationError, "Capture source is invalid");
        if (string.IsNullOrWhiteSpace(mimeType) || mimeType.Length > MaxMimeTypeLength)
            throw new DomainException(ErrorCodes.ValidationError, $"MIME type is required and cannot exceed {MaxMimeTypeLength} characters");
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > MaxFileNameLength)
            throw new DomainException(ErrorCodes.ValidationError, $"File name is required and cannot exceed {MaxFileNameLength} characters");
        if (byteSize <= 0)
            throw new DomainException(ErrorCodes.ValidationError, "Artefact byte size must be greater than zero");
        if (sha256.Length != Sha256HexLength || sha256.Any(c => !Uri.IsHexDigit(c)))
            throw new DomainException(ErrorCodes.ValidationError, "SHA-256 must be a 64-character hexadecimal digest");
        if (originReference is not null && originReference.Length > MaxOriginReferenceLength)
            throw new DomainException(ErrorCodes.ValidationError, $"Origin reference cannot exceed {MaxOriginReferenceLength} characters");

        UserId = userId;
        BoardId = boardId;
        Kind = kind;
        MimeType = mimeType;
        FileName = fileName;
        ByteSize = byteSize;
        Sha256 = sha256.ToLowerInvariant();
        CaptureSource = captureSource;
        OriginReference = string.IsNullOrWhiteSpace(originReference) ? null : originReference;
        CreatedFromCaptureId = createdFromCaptureId;
    }
}
