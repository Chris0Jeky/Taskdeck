namespace Taskdeck.Application.Interfaces;

/// <summary>
/// Generates fixed-dimension embedding vectors from text input.
/// All implementations must run locally -- no user content may leave the machine.
/// </summary>
public interface IEmbeddingGenerator
{
    /// <summary>
    /// The dimensionality of vectors produced by this generator.
    /// Callers use this to pre-allocate storage and validate index compatibility.
    /// </summary>
    int Dimensions { get; }

    /// <summary>
    /// Whether the generator is ready to produce embeddings.
    /// Returns false when the underlying model failed to load or dependencies
    /// are unavailable, allowing callers to fall back to FTS.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Generates an embedding vector for a single text input.
    /// </summary>
    Task<ReadOnlyMemory<float>> GenerateAsync(
        string text,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates embedding vectors for a batch of text inputs.
    /// The returned list is positionally aligned with the input list.
    /// </summary>
    Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);
}
