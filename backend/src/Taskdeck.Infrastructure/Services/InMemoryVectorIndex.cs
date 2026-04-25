using System.Collections.Concurrent;
using System.Numerics;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;

namespace Taskdeck.Infrastructure.Services;

/// <summary>
/// Thread-safe, in-memory vector index for development and testing.
/// Uses brute-force cosine similarity -- suitable for small collections
/// (&lt;100k vectors) but not production-scale workloads.
/// </summary>
public sealed class InMemoryVectorIndex : IVectorIndex
{
    private readonly ConcurrentDictionary<string, StoredVector> _vectors = new();

    public Task UpsertAsync(
        string documentId,
        ReadOnlyMemory<float> vector,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

        var stored = new StoredVector(vector.ToArray(), metadata);
        _vectors.AddOrUpdate(documentId, stored, (_, _) => stored);
        return Task.CompletedTask;
    }

    public Task UpsertBatchAsync(
        IReadOnlyList<VectorDocument> documents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);

        foreach (var doc in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stored = new StoredVector(doc.Vector.ToArray(), doc.Metadata);
            _vectors.AddOrUpdate(doc.DocumentId, stored, (_, _) => stored);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<VectorSearchResult>> QueryAsync(
        ReadOnlyMemory<float> queryVector,
        int topK = 10,
        IReadOnlyDictionary<string, string>? filter = null,
        CancellationToken cancellationToken = default)
    {
        if (topK <= 0)
        {
            IReadOnlyList<VectorSearchResult> empty = Array.Empty<VectorSearchResult>();
            return Task.FromResult(empty);
        }

        var querySpan = queryVector.Span;
        var results = new List<(string Id, double Score, IReadOnlyDictionary<string, string>? Meta)>();

        foreach (var kvp in _vectors)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (filter is not null && !MatchesFilter(kvp.Value.Metadata, filter))
                continue;

            var score = CosineSimilarity(querySpan, kvp.Value.Values.AsSpan());
            results.Add((kvp.Key, score, kvp.Value.Metadata));
        }

        // Sort descending by score (higher = more similar), take topK
        results.Sort((a, b) => b.Score.CompareTo(a.Score));

        IReadOnlyList<VectorSearchResult> topResults = results
            .Take(topK)
            .Select(r => new VectorSearchResult(r.Id, r.Score, r.Meta))
            .ToList();

        return Task.FromResult(topResults);
    }

    public Task DeleteAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        _vectors.TryRemove(documentId, out _);
        return Task.CompletedTask;
    }

    public Task DeleteBatchAsync(
        IReadOnlyList<string> documentIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentIds);

        foreach (var id in documentIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _vectors.TryRemove(id, out _);
        }

        return Task.CompletedTask;
    }

    public Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult((long)_vectors.Count);
    }

    internal static double CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0.0;

        // Use SIMD-accelerated operations when available
        float dot = 0f, normA = 0f, normB = 0f;

        int i = 0;
        int simdLength = Vector<float>.Count;

        if (Vector.IsHardwareAccelerated && a.Length >= simdLength)
        {
            var dotVec = Vector<float>.Zero;
            var normAVec = Vector<float>.Zero;
            var normBVec = Vector<float>.Zero;

            for (; i <= a.Length - simdLength; i += simdLength)
            {
                var va = new Vector<float>(a.Slice(i, simdLength));
                var vb = new Vector<float>(b.Slice(i, simdLength));
                dotVec += va * vb;
                normAVec += va * va;
                normBVec += vb * vb;
            }

            dot = Vector.Sum(dotVec);
            normA = Vector.Sum(normAVec);
            normB = Vector.Sum(normBVec);
        }

        // Handle remaining elements
        for (; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denominator = Math.Sqrt(normA) * Math.Sqrt(normB);
        if (denominator < 1e-10)
            return 0.0;

        return dot / denominator;
    }

    private static bool MatchesFilter(
        IReadOnlyDictionary<string, string>? metadata,
        IReadOnlyDictionary<string, string> filter)
    {
        if (metadata is null)
            return false;

        foreach (var kvp in filter)
        {
            if (!metadata.TryGetValue(kvp.Key, out var value) || value != kvp.Value)
                return false;
        }

        return true;
    }

    private sealed record StoredVector(float[] Values, IReadOnlyDictionary<string, string>? Metadata);
}
