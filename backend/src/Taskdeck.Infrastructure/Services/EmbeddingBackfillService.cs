using Microsoft.Extensions.Logging;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Infrastructure.Services;

/// <summary>
/// Scans knowledge chunks and ensures they have vector embeddings.
/// Idempotent: uses upsert so re-processing is safe across restarts.
/// Failure-safe: individual item errors are logged and skipped, not rethrown.
/// Note: currently loads all chunks per batch; a future optimization could
/// track embedded status in the database to skip already-processed chunks.
/// </summary>
public sealed class EmbeddingBackfillService : IEmbeddingBackfillService
{
    private readonly IVectorIndex _vectorIndex;
    private readonly IEmbeddingGenerator _embeddingGenerator;
    private readonly IKnowledgeChunkRepository _chunkRepository;
    private readonly ILogger<EmbeddingBackfillService> _logger;

    public EmbeddingBackfillService(
        IVectorIndex vectorIndex,
        IEmbeddingGenerator embeddingGenerator,
        IKnowledgeChunkRepository chunkRepository,
        ILogger<EmbeddingBackfillService> logger)
    {
        _vectorIndex = vectorIndex;
        _embeddingGenerator = embeddingGenerator;
        _chunkRepository = chunkRepository;
        _logger = logger;
    }

    public async Task<BackfillBatchResult> ProcessBatchAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (!_embeddingGenerator.IsAvailable)
        {
            _logger.LogDebug("Embedding generator is not available; skipping backfill batch");
            return new BackfillBatchResult(Processed: 0, Failed: 0, Remaining: 0);
        }

        // Get all knowledge chunks. The current approach loads all chunks and
        // re-upserts them (idempotent). A future optimization could track embedded
        // status via a database flag or separate table to skip already-processed
        // chunks. For small-to-medium collections (<100k) this is acceptable.
        var allChunks = await _chunkRepository.GetAllAsync(cancellationToken);
        var chunkList = allChunks.ToList();

        var toProcess = chunkList.Take(batchSize).ToList();
        int processed = 0;
        int failed = 0;

        foreach (var chunk in toProcess)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var text = chunk.Content;
                if (string.IsNullOrWhiteSpace(text))
                {
                    _logger.LogDebug(
                        "Skipping empty chunk {ChunkId}",
                        chunk.Id);
                    continue;
                }

                var embedding = await _embeddingGenerator.GenerateAsync(text, cancellationToken);

                var metadata = new Dictionary<string, string>
                {
                    ["type"] = "knowledge_chunk",
                    ["documentId"] = chunk.DocumentId.ToString(),
                    ["chunkId"] = chunk.Id.ToString()
                };

                await _vectorIndex.UpsertAsync(
                    $"chunk:{chunk.Id}",
                    embedding,
                    metadata,
                    cancellationToken);

                processed++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogWarning(
                    "Failed to embed chunk {ChunkId}: {Error}",
                    chunk.Id,
                    ex.Message);
            }
        }

        int remaining = Math.Max(0, chunkList.Count - batchSize);

        _logger.LogInformation(
            "Embedding backfill batch complete: {Processed} processed, {Failed} failed, ~{Remaining} remaining",
            processed,
            failed,
            remaining);

        return new BackfillBatchResult(processed, failed, remaining);
    }
}
