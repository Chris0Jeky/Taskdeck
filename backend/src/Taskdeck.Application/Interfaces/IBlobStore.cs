using Taskdeck.Domain.Enums;

namespace Taskdeck.Application.Interfaces;

/// <summary>
/// Content-addressed stored bytes, owner-scoped. An object carries no media type: identical bytes
/// may arrive with different declared media types, so the media type belongs to the
/// <see cref="Domain.Entities.SourceAsset"/> (or artefact) that references the object.
/// </summary>
public sealed record BlobObjectDescriptor(
    Guid BlobObjectId,
    Guid OwnerUserId,
    string ContentHash,
    long ByteSize,
    int ReferenceCount);

/// <summary>
/// One holder's claim on a blob object. A source asset, an artefact or a retention job holds a
/// reference; the bytes live exactly as long as at least one reference does.
/// </summary>
public sealed record BlobReference(
    Guid ReferenceId,
    Guid BlobObjectId,
    Guid OwnerUserId,
    string ContentHash,
    long ByteSize,
    CaptureModality AssetModality,
    DateTimeOffset AcquiredAt);

/// <summary>
/// What a caller declares before streaming bytes in. <see cref="ExpectedByteSize"/> is both a quota
/// reservation and a hard cap: the store reserves the per-owner and per-modality quota for it
/// before it consumes the stream and rejects a stream that grows past it, so an unbounded upload
/// can never be discovered only after it was stored. The referrer names the row that will hold the
/// reference (a source asset today), for audit and for orphan sweeps.
/// </summary>
public sealed record BlobAcquisition(
    Guid OwnerUserId,
    CaptureModality AssetModality,
    long ExpectedByteSize,
    string? ReferrerKind,
    Guid? ReferrerId);

/// <summary>Per-owner storage accounting; the per-modality split is what per-kind quotas and backup-size reporting read.</summary>
public sealed record BlobQuotaUsage(
    Guid OwnerUserId,
    long TotalBytes,
    IReadOnlyDictionary<CaptureModality, long> BytesByModality,
    int ObjectCount,
    int ReferenceCount);

/// <summary>
/// Storage seam for source bytes (ADR-0065 §Decision 11; CF-23 <c>#2276</c>; amended 2026-08-30 to
/// reference semantics). The local implementation is SQLite-backed, which keeps the single-file
/// ownership promise of ADR-0046 decision 4; an object-store implementation is admissible only at
/// ADR-0061 stage 3. Objects are content-addressed <b>per owner</b>: the same bytes uploaded twice
/// by one user are stored once and referenced twice, and never deduplicated across users
/// (isolation over savings). Deletion is therefore never "delete by hash" — a holder releases its
/// reference, and the object is removed inside the caller's ambient transaction only when the last
/// reference goes.
/// <para>
/// Stream ownership is explicit: the caller owns the stream it passes to <see cref="AcquireAsync"/>
/// and disposes it afterwards; the caller owns and disposes the stream <see cref="OpenReadAsync"/>
/// returns. Note for CF-23: the shipped <c>ArtefactBlob.Content</c> is a <c>byte[]</c>, and putting
/// it behind this interface does not make it streaming storage — a large-audio implementation needs
/// SQLite incremental BLOB I/O, bounded chunk rows, or a controlled spool-then-store step; the
/// contract tests must include an input larger than the in-memory buffer.
/// No implementation is registered yet — CF-23 wires <c>SqliteBlobStore</c>.
/// </para>
/// </summary>
public interface IBlobStore
{
    /// <summary>
    /// Stores the bytes (or, when an object with the same hash already exists for the owner, discards
    /// them) and returns a new reference. Reserves quota for <see cref="BlobAcquisition.ExpectedByteSize"/>
    /// before reading; fails closed with a stable quota or size error before anything is persisted.
    /// </summary>
    Task<BlobReference> AcquireAsync(
        BlobAcquisition acquisition,
        Stream content,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a reference to an object the owner already holds (a second asset over the same bytes) without re-streaming.</summary>
    Task<BlobReference?> AcquireExistingAsync(
        Guid ownerUserId,
        string contentHash,
        CaptureModality assetModality,
        string? referrerKind,
        Guid? referrerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops one reference. Owner-scoped like every other operation: the reference must belong to
    /// <paramref name="ownerUserId"/> or the call fails without mutating anything, so a leaked or
    /// mistaken foreign reference id can never decrement another user's count or delete their
    /// bytes. Returns true when it was the last reference and the object's bytes were removed in the
    /// same transaction; false when other references keep the object alive.
    /// </summary>
    Task<bool> ReleaseAsync(
        Guid referenceId,
        Guid ownerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Opens the bytes for an owner-scoped read; null when the object does not exist for that owner.</summary>
    Task<Stream?> OpenReadAsync(
        Guid blobObjectId,
        Guid ownerUserId,
        CancellationToken cancellationToken = default);

    Task<BlobObjectDescriptor?> FindByHashAsync(
        Guid ownerUserId,
        string contentHash,
        CancellationToken cancellationToken = default);

    Task<BlobQuotaUsage> GetUsageAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken = default);
}
