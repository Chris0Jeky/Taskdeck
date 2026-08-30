namespace Taskdeck.Application.Interfaces;

/// <summary>Content-addressed description of a stored blob.</summary>
public sealed record BlobDescriptor(string ContentHash, long ByteSize, string MediaType);

/// <summary>
/// Storage seam for source bytes (ADR-0065 §Decision 11; CF-23 <c>#2276</c>). The local
/// implementation is SQLite-backed — today's <c>ArtefactBlob</c> table — which keeps the single-file
/// ownership promise of ADR-0046 decision 4; an object-store implementation is admissible only at
/// ADR-0061 stage 3. Blobs are owner-scoped: deduplication happens per owner, never across users
/// (isolation over savings). Streams are used on both sides so audio never has to be buffered whole.
/// No implementation is registered yet — CF-23 wires <c>SqliteBlobStore</c>.
/// </summary>
public interface IBlobStore
{
    Task<BlobDescriptor> PutAsync(
        Guid ownerUserId,
        Stream content,
        string mediaType,
        CancellationToken cancellationToken = default);

    Task<Stream?> OpenReadAsync(
        Guid ownerUserId,
        string contentHash,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        Guid ownerUserId,
        string contentHash,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        Guid ownerUserId,
        string contentHash,
        CancellationToken cancellationToken = default);

    Task<long> GetTotalBytesAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken = default);
}
