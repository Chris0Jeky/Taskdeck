using Microsoft.Extensions.Logging;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Infrastructure.Services;

/// <summary>
/// Scans knowledge chunks and ensures they have vector embeddings.
/// Tracks indexed chunk IDs via a process-local set to avoid re-processing.
/// Prunes stale vectors whose chunks no longer exist in the repository.
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
    // Bounded: pruned when stale chunks are detected.
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

        // Load all chunks once per batch to build the current ID set.
        // TODO: When IKnowledgeChunkRepository supports paginated queries
        // (e.g. GetUnembeddedAsync(skip, take)), switch to that to avoid
        // full-table reads on large datasets.
        var allChunks = await _chunkRepository.GetAllAsync(cancellationToken);
        var chunkList = allChunks.ToList();

        // Build a set of current chunk IDs for stale-vector detection
        var currentChunkIds = new HashSet<string>(
            chunkList.Select(c => c.Id.ToString()));

        // Prune stale vectors: remove indexed entries whose chunks
        // no longer exist (document was edited and chunks recreated
        // with new IDs).
        await PruneStaleVectorsAsync(currentChunkIds, cancellationToken);

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

        // Separate embeddable chunks from empty ones
        var embeddable = new List<(Domain.Entities.KnowledgeChunk Chunk, string Text)>();
        foreach (var chunk in toProcess)
        {
            var text = chunk.Content;
            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogDebug("Skipping empty chunk {ChunkId}", chunk.Id);
                lock (_indexedLock) { _indexedChunkIds.Add(chunk.Id.ToString()); }
                continue;
            }
            embeddable.Add((chunk, text));
        }

        if (embeddable.Count == 0)
        {
            int emptyRemaining = Math.Max(0, unindexed.Count - toProcess.Count);
            return new BackfillBatchResult(Processed: 0, Failed: 0, Remaining: emptyRemaining);
        }

        // Use batch embedding for better throughput
        var texts = embeddable.Select(e => e.Text).ToList();
        IReadOnlyList<ReadOnlyMemory<float>> embeddings;
        try
        {
            embeddings = await _embeddingGenerator.GenerateBatchAsync(texts, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // If batch embedding fails entirely, fall back to one-by-one
            _logger.LogWarning(
                "Batch embedding failed, falling back to individual embedding: {Error}",
                ex.Message);
            return await ProcessOneByOneAsync(embeddable, documentMap, unindexed.Count, toProcess.Count, cancellationToken);
        }

        int processed = 0;
        int failed = 0;

        // Build batch-upsert documents and track which chunk IDs they map to.
        // Chunks are NOT marked as indexed here -- only after successful upsert.
        var vectorDocs = new List<VectorDocument>();
        var chunkIdsByDocId = new Dictionary<string, string>();
        for (int i = 0; i < embeddable.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (chunk, _) = embeddable[i];
            try
            {
                var metadata = BuildMetadata(chunk, documentMap);
                var docId = $"chunk:{chunk.Id}";

                vectorDocs.Add(new VectorDocument(
                    docId,
                    embeddings[i],
                    metadata));

                chunkIdsByDocId[docId] = chunk.Id.ToString();
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogWarning(
                    "Failed to prepare chunk {ChunkId} for upsert: {Error}",
                    chunk.Id,
                    ex.Message);
            }
        }

        // Batch upsert all vectors at once, then mark as indexed on success
        if (vectorDocs.Count > 0)
        {
            try
            {
                await _vectorIndex.UpsertBatchAsync(vectorDocs, cancellationToken);

                // Batch succeeded -- mark all chunks as indexed
                processed = vectorDocs.Count;
                lock (_indexedLock)
                {
                    foreach (var chunkId in chunkIdsByDocId.Values)
                        _indexedChunkIds.Add(chunkId);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Batch upsert failed, falling back to individual upserts: {Error}",
                    ex.Message);

                // Fall back to individual upserts -- mark each chunk only on success
                foreach (var doc in vectorDocs)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        await _vectorIndex.UpsertAsync(
                            doc.DocumentId, doc.Vector, doc.Metadata, cancellationToken);

                        processed++;
                        if (chunkIdsByDocId.TryGetValue(doc.DocumentId, out var chunkId))
                        {
                            lock (_indexedLock) { _indexedChunkIds.Add(chunkId); }
                        }
                    }
                    catch (Exception upsertEx)
                    {
                        failed++;
                        _logger.LogWarning(
                            "Individual upsert failed for {DocId}: {Error}",
                            doc.DocumentId, upsertEx.Message);
                    }
                }
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

    /// <summary>
    /// Removes vectors from the index whose chunk IDs are no longer present
    /// in the repository. Also removes them from the tracked set so the
    /// static set stays bounded.
    /// </summary>
    private async Task PruneStaleVectorsAsync(
        HashSet<string> currentChunkIds,
        CancellationToken cancellationToken)
    {
        List<string> staleKeys;
        lock (_indexedLock)
        {
            staleKeys = _indexedChunkIds
                .Where(id => !currentChunkIds.Contains(id))
                .ToList();
        }

        if (staleKeys.Count == 0)
            return;

        // Delete stale vectors from the index
        var staleDocIds = staleKeys.Select(id => $"chunk:{id}").ToList();
        try
        {
            await _vectorIndex.DeleteBatchAsync(staleDocIds, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Failed to prune {Count} stale vectors: {Error}",
                staleDocIds.Count, ex.Message);
            return; // Don't remove from tracking if delete failed
        }

        // Remove from tracking set
        lock (_indexedLock)
        {
            foreach (var key in staleKeys)
                _indexedChunkIds.Remove(key);
        }

        _logger.LogInformation(
            "Pruned {Count} stale vectors from the index",
            staleKeys.Count);
    }

    /// <summary>
    /// Fallback: process chunks one-by-one when batch embedding fails.
    /// </summary>
    private async Task<BackfillBatchResult> ProcessOneByOneAsync(
        List<(Domain.Entities.KnowledgeChunk Chunk, string Text)> embeddable,
        Dictionary<Guid, Domain.Entities.KnowledgeDocument> documentMap,
        int totalUnindexed,
        int batchCount,
        CancellationToken cancellationToken)
    {
        int processed = 0;
        int failed = 0;

        foreach (var (chunk, text) in embeddable)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var embedding = await _embeddingGenerator.GenerateAsync(text, cancellationToken);
                var metadata = BuildMetadata(chunk, documentMap);

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

        int remaining = Math.Max(0, totalUnindexed - batchCount);

        _logger.LogInformation(
            "Embedding backfill batch complete (one-by-one fallback): {Processed} processed, {Failed} failed, ~{Remaining} remaining",
            processed, failed, remaining);

        return new BackfillBatchResult(processed, failed, remaining);
    }

    private static Dictionary<string, string> BuildMetadata(
        Domain.Entities.KnowledgeChunk chunk,
        Dictionary<Guid, Domain.Entities.KnowledgeDocument> documentMap)
    {
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

        return metadata;
    }
}
