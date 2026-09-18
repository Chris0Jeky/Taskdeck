using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>Owner-scoped bytes, stored in bounded SQLite chunks. A null hash is an uncommitted upload.</summary>
public sealed class StoredBlob
{
    public Guid Id { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public string? ContentHash { get; private set; }
    public long ByteSize { get; private set; }
    private StoredBlob() { }
    public StoredBlob(Guid ownerUserId, long byteSize)
    {
        if (ownerUserId == Guid.Empty || byteSize <= 0)
            throw new DomainException(ErrorCodes.ValidationError, "A blob requires an owner and positive size.");
        Id = Guid.NewGuid(); OwnerUserId = ownerUserId; ByteSize = byteSize;
    }
    public void Complete(string hash)
    {
        if (ContentHash is not null || hash.Length != 64 || hash.Any(c => !Uri.IsHexDigit(c)))
            throw new DomainException(ErrorCodes.ValidationError, "A blob may be completed once with its content hash.");
        ContentHash = hash.ToLowerInvariant();
    }
}

public sealed class StoredBlobChunk
{
    public const int MaximumSize = 65_536;
    public Guid BlobId { get; private set; }
    public int Ordinal { get; private set; }
    public byte[] Content { get; private set; } = [];
    private StoredBlobChunk() { }
    public StoredBlobChunk(Guid blobId, int ordinal, byte[] content)
    {
        if (blobId == Guid.Empty || ordinal < 0 || content.Length is < 1 or > MaximumSize)
            throw new DomainException(ErrorCodes.ValidationError, "Invalid stored byte chunk.");
        BlobId = blobId; Ordinal = ordinal; Content = content;
    }
}

/// <summary>One holder's claim; bytes survive while any reference for their owner remains.</summary>
public sealed class StoredBlobReference
{
    public Guid Id { get; private set; }
    public Guid BlobId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public CaptureModality Modality { get; private set; }
    public string? ReferrerKind { get; private set; }
    public Guid? ReferrerId { get; private set; }
    public DateTimeOffset AcquiredAt { get; private set; }
    private StoredBlobReference() { }
    public StoredBlobReference(StoredBlob blob, CaptureModality modality, string? referrerKind, Guid? referrerId)
    {
        if (blob.ContentHash is null || !Enum.IsDefined(modality) || referrerKind?.Length > 100 || referrerId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Invalid blob reference.");
        Id = Guid.NewGuid(); BlobId = blob.Id; OwnerUserId = blob.OwnerUserId; Modality = modality;
        ReferrerKind = referrerKind; ReferrerId = referrerId; AcquiredAt = DateTimeOffset.UtcNow;
    }
}
