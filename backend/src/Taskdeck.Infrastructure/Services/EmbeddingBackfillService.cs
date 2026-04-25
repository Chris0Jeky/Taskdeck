using Microsoft.Extensions.Logging;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Infrastructure.Services;

/// <summary>
/// Scans knowledge chunks and ensures they have vector embeddings.
/// Tracks progress via a process-local (CreatedAt, Id) cursor to avoid re-processing.
/// Fetches only later pages and prunes stale vectors whose tracked chunks no
/// longer exist in the repository.
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
    private static readonly HashSet<Guid> _indexedChunkIds = new();
    private static readonly object _indexedLock = new();
    private static KnowledgeChunkBackfillCursor? _processedThrough;

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

    public static void ResetProgressForTests()
    {
        lock (_indexedLock)
        {
            _indexedChunkIds.Clear();
            _processedThrough = null;
        }
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

        await PruneStaleVectorsAsync(cancellationToken);

        var processedThrough = GetProcessedThrough();

        var toProcess = (await _chunkRepository.GetUnindexedBatchAsync(
            processedThrough,
            batchSize,
            cancellationToken)).ToList();

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
        var completedChunkIds = new HashSet<Guid>();
        foreach (var chunk in toProcess)
        {
            var text = chunk.Content;
            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogDebug("Skipping empty chunk {ChunkId}", chunk.Id);
                completedChunkIds.Add(chunk.Id);
                continue;
            }
            embeddable.Add((chunk, text));
        }

        if (embeddable.Count == 0)
        {
            AdvanceProcessedThroughCompletedPrefix(toProcess, completedChunkIds);
            var emptyRemaining = await CountRemainingAsync(cancellationToken);
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
            return await ProcessOneByOneAsync(embeddable, toProcess, completedChunkIds, documentMap, cancellationToken);
        }

        int processed = 0;
        int failed = 0;

        // Build batch-upsert documents and track which chunk IDs they map to.
        // Chunks are NOT marked as indexed here -- only after successful upsert.
        var vectorDocs = new List<VectorDocument>();
        var chunkIdsByDocId = new Dictionary<string, Guid>();
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

                chunkIdsByDocId[docId] = chunk.Id;
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
                    {
                        _indexedChunkIds.Add(chunkId);
                        completedChunkIds.Add(chunkId);
                    }
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
                            TrackIndexedChunks(new[] { chunkId });
                            completedChunkIds.Add(chunkId);
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

        AdvanceProcessedThroughCompletedPrefix(toProcess, completedChunkIds);
        var remaining = await CountRemainingAsync(cancellationToken);

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
    private async Task PruneStaleVectorsAsync(CancellationToken cancellationToken)
    {
        List<Guid> trackedIds;
        lock (_indexedLock)
        {
            trackedIds = _indexedChunkIds.ToList();
        }

        if (trackedIds.Count == 0)
            return;

        var existingIds = await _chunkRepository.GetExistingIdsAsync(
            trackedIds,
            cancellationToken);
        var staleKeys = trackedIds
            .Where(id => !existingIds.Contains(id))
            .ToList();

        if (staleKeys.Count == 0)
            return;

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
        IReadOnlyList<Domain.Entities.KnowledgeChunk> batchChunks,
        HashSet<Guid> completedChunkIds,
        Dictionary<Guid, Domain.Entities.KnowledgeDocument> documentMap,
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
                TrackIndexedChunks(new[] { chunk.Id });
                completedChunkIds.Add(chunk.Id);
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

        AdvanceProcessedThroughCompletedPrefix(batchChunks, completedChunkIds);
        var remaining = await CountRemainingAsync(cancellationToken);

        _logger.LogInformation(
            "Embedding backfill batch complete (one-by-one fallback): {Processed} processed, {Failed} failed, ~{Remaining} remaining",
            processed, failed, remaining);

        return new BackfillBatchResult(processed, failed, remaining);
    }

    private async Task<int> CountRemainingAsync(CancellationToken cancellationToken)
    {
        return await _chunkRepository.CountUnindexedAsync(
            GetProcessedThrough(),
            cancellationToken);
    }

    private static KnowledgeChunkBackfillCursor? GetProcessedThrough()
    {
        lock (_indexedLock)
        {
            return _processedThrough;
        }
    }

    private static void TrackIndexedChunks(IEnumerable<Guid> chunkIds)
    {
        lock (_indexedLock)
        {
            foreach (var chunkId in chunkIds)
                _indexedChunkIds.Add(chunkId);
        }
    }

    private static void AdvanceProcessedThroughCompletedPrefix(
        IReadOnlyList<Domain.Entities.KnowledgeChunk> chunks,
        IReadOnlySet<Guid> completedChunkIds)
    {
        if (chunks.Count == 0)
            return;

        KnowledgeChunkBackfillCursor? candidate = null;
        foreach (var chunk in chunks.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id))
        {
            if (!completedChunkIds.Contains(chunk.Id))
                break;

            candidate = new KnowledgeChunkBackfillCursor(chunk.CreatedAt, chunk.Id);
        }

        if (candidate is null)
            return;

        lock (_indexedLock)
        {
            if (_processedThrough is null || IsAfterCursor(candidate, _processedThrough))
                _processedThrough = candidate;
        }
    }

    private static bool IsAfterCursor(
        KnowledgeChunkBackfillCursor candidate,
        KnowledgeChunkBackfillCursor current)
    {
        if (candidate.CreatedAt > current.CreatedAt)
            return true;

        return candidate.CreatedAt == current.CreatedAt && candidate.Id.CompareTo(current.Id) > 0;
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
