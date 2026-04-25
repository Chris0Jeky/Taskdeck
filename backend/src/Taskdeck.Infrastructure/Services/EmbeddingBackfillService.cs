using Microsoft.Extensions.Logging;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Infrastructure.Services;

/// <summary>
/// Scans knowledge chunks and ensures they have vector embeddings.
/// Tracks indexed chunk IDs via a process-local set to avoid re-processing.
/// Failure-safe: individual item errors are logged and skipped, not rethrown.
/// </summary>
public sealed class EmbeddingBackfillService : IEmbeddingBackfillService
{
    private readonly IVectorIndex _vectorIndex;
    private readonly IEmbeddingGenerator _embeddingGenerator;
    private readonly IKnowledgeChunkRepository _chunkRepository;
    private readonly IKnowledgeDocumentRepository _documentRepository;
    private readonly ILogger<EmbeddingBackfillService> _logger;

    // Tracks which chunk IDs have already been embedded to avoid re-processing.
    // Static so it persists across scoped service instances within the same
    // process lifetime. Thread-safe via lock.
    private static readonly HashSet<string> _indexedChunkIds = new();
    private static readonly object _indexedLock = new();

    public EmbeddingBackfillService(
        IVectorIndex vectorIndex,
        IEmbeddingGenerator embeddingGenerator,
        IKnowledgeChunkRepository chunkRepository,
        IKnowledgeDocumentRepository documentRepository,
        ILogger<EmbeddingBackfillService> logger)
    {
        _vectorIndex = vectorIndex;
        _embeddingGenerator = embeddingGenerator;
        _chunkRepository = chunkRepository;
        _documentRepository = documentRepository;
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

        var allChunks = await _chunkRepository.GetAllAsync(cancellationToken);
        var chunkList = allChunks.ToList();

        // Filter out chunks that have already been indexed in this process lifetime
        List<Domain.Entities.KnowledgeChunk> unindexed;
        lock (_indexedLock)
        {
            unindexed = chunkList
                .Where(c => !_indexedChunkIds.Contains(c.Id.ToString()))
                .ToList();
        }

        var toProcess = unindexed.Take(batchSize).ToList();

        if (toProcess.Count == 0)
        {
            return new BackfillBatchResult(Processed: 0, Failed: 0, Remaining: 0);
        }

        // Pre-fetch parent documents for userId/boardId metadata.
        // Group by DocumentId to minimize repository lookups.
        var documentIds = toProcess.Select(c => c.DocumentId).Distinct().ToList();
        var documentMap = new Dictionary<Guid, Domain.Entities.KnowledgeDocument>();
        foreach (var docId in documentIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var doc = await _documentRepository.GetByIdAsync(docId, cancellationToken);
            if (doc is not null)
                documentMap[docId] = doc;
        }

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
                    lock (_indexedLock) { _indexedChunkIds.Add(chunk.Id.ToString()); }
                    continue;
                }

                var embedding = await _embeddingGenerator.GenerateAsync(text, cancellationToken);

                var metadata = new Dictionary<string, string>
                {
                    ["type"] = "knowledge_chunk",
                    ["documentId"] = chunk.DocumentId.ToString(),
                    ["chunkId"] = chunk.Id.ToString()
                };

                // Include userId and boardId for access-control filtering
                if (documentMap.TryGetValue(chunk.DocumentId, out var parentDoc))
                {
                    metadata["userId"] = parentDoc.UserId.ToString();
                    if (parentDoc.BoardId.HasValue)
                        metadata["boardId"] = parentDoc.BoardId.Value.ToString();
                }

                await _vectorIndex.UpsertAsync(
                    $"chunk:{chunk.Id}",
                    embedding,
                    metadata,
                    cancellationToken);

                processed++;
                lock (_indexedLock) { _indexedChunkIds.Add(chunk.Id.ToString()); }
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

        int remaining = Math.Max(0, unindexed.Count - toProcess.Count);

        _logger.LogInformation(
            "Embedding backfill batch complete: {Processed} processed, {Failed} failed, ~{Remaining} remaining",
            processed,
            failed,
            remaining);

        return new BackfillBatchResult(processed, failed, remaining);
    }
}
