using Taskdeck.Application.Interfaces;

namespace Taskdeck.Infrastructure.Services;

/// <summary>
/// Deterministic, hash-based embedding generator for testing and development.
/// Produces consistent vectors for the same input text using a simple hash-based
/// projection. Not suitable for production semantic search but preserves the
/// property that identical texts produce identical vectors.
/// </summary>
public sealed class InMemoryEmbeddingGenerator : IEmbeddingGenerator
{
    private readonly int _dimensions;

    public InMemoryEmbeddingGenerator(int dimensions = 384)
    {
        if (dimensions <= 0)
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Dimensions must be positive.");

        _dimensions = dimensions;
    }

    public int Dimensions => _dimensions;

    public bool IsAvailable => true;

    public Task<ReadOnlyMemory<float>> GenerateAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        var vector = GenerateVector(text);
        return Task.FromResult<ReadOnlyMemory<float>>(vector);
    }

    public Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);

        var results = new List<ReadOnlyMemory<float>>(texts.Count);

        foreach (var text in texts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(GenerateVector(text));
        }

        return Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>(results);
    }

    /// <summary>
    /// Generates a deterministic, normalized vector from text using hash-based projection.
    /// The same text always produces the same vector.
    /// </summary>
    private float[] GenerateVector(string text)
    {
        var vector = new float[_dimensions];

        if (string.IsNullOrEmpty(text))
            return vector;

        // Use a simple hash-based approach to fill vector dimensions deterministically
        var hash = GetStableHash(text);

        for (int i = 0; i < _dimensions; i++)
        {
            // Mix the hash with the dimension index for per-dimension variation
            var mixed = hash ^ (uint)(i * 2654435761); // Knuth's multiplicative hash constant
            // Map to [-1, 1] range
            vector[i] = (mixed / (float)uint.MaxValue) * 2f - 1f;
        }

        // L2-normalize so cosine similarity is meaningful
        Normalize(vector);

        return vector;
    }

    /// <summary>
    /// Produces a stable 32-bit hash from a string that does not depend on
    /// runtime-specific GetHashCode implementations.
    /// </summary>
    private static uint GetStableHash(string text)
    {
        // FNV-1a hash -- deterministic across runs and runtimes
        uint hash = 2166136261;
        foreach (var c in text)
        {
            hash ^= c;
            hash *= 16777619;
        }
        return hash;
    }

    private static void Normalize(float[] vector)
    {
        float norm = 0f;
        for (int i = 0; i < vector.Length; i++)
            norm += vector[i] * vector[i];

        norm = MathF.Sqrt(norm);
        if (norm < 1e-10f)
            return;

        for (int i = 0; i < vector.Length; i++)
            vector[i] /= norm;
    }
}
