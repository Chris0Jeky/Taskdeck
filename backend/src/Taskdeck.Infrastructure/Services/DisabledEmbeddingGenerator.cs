using Taskdeck.Application.Interfaces;

namespace Taskdeck.Infrastructure.Services;

/// <summary>
/// Embedding generator used when no production embedding provider is configured.
/// Keeps semantic search/backfill safely disabled so callers fall back to FTS.
/// </summary>
public sealed class DisabledEmbeddingGenerator : IEmbeddingGenerator
{
    public int Dimensions => 0;

    public bool IsAvailable => false;

    public Task<ReadOnlyMemory<float>> GenerateAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Embedding generation is disabled.");
    }

    public Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Embedding generation is disabled.");
    }
}
