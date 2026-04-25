using Taskdeck.Application.DTOs;

namespace Taskdeck.Application.Interfaces;

/// <summary>
/// Abstracts vector storage and nearest-neighbor search so the backing store
/// (in-memory, sqlite-vec, external service) is swappable without changing
/// application-layer code.
///
/// All vectors are keyed by a string document identifier.
/// Implementations must be thread-safe for concurrent read/write.
/// </summary>
public interface IVectorIndex
{
    /// <summary>
    /// Upserts a single document embedding into the index.
    /// If a vector with the same <paramref name="documentId"/> already exists
    /// it is replaced atomically.
    /// </summary>
    Task UpsertAsync(
        string documentId,
        ReadOnlyMemory<float> vector,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts multiple document embeddings in a single batch.
    /// Implementations should optimize for throughput over latency.
    /// </summary>
    Task UpsertBatchAsync(
        IReadOnlyList<VectorDocument> documents,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the <paramref name="topK"/> nearest neighbors of the given
    /// <paramref name="queryVector"/>.
    /// </summary>
    Task<IReadOnlyList<VectorSearchResult>> QueryAsync(
        ReadOnlyMemory<float> queryVector,
        int topK = 10,
        IReadOnlyDictionary<string, string>? filter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the vector associated with <paramref name="documentId"/>.
    /// No-op if the document does not exist.
    /// </summary>
    Task DeleteAsync(
        string documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all vectors whose document IDs match any of the given IDs.
    /// </summary>
    Task DeleteBatchAsync(
        IReadOnlyList<string> documentIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the number of vectors currently stored in the index.
    /// </summary>
    Task<long> CountAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Batch-upsert payload for <see cref="IVectorIndex.UpsertBatchAsync"/>.
/// </summary>
public sealed record VectorDocument(
    string DocumentId,
    ReadOnlyMemory<float> Vector,
    IReadOnlyDictionary<string, string>? Metadata = null);
