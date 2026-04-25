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
    private const int MaxStalePruneProbeCount = 500;

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
    private static readonly HashSet<Guid> _deferredRetryChunkIds = new();
    private static readonly HashSet<Guid> _processedIdsAtCursorTimestamp = new();
    private static readonly object _indexedLock = new();
    private static KnowledgeChunkBackfillCursor? _processedThrough;
    private static int _nextPruneProbeStartIndex;
    private static int _nextDeferredRetryStartIndex;

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
            _deferredRetryChunkIds.Clear();
            _processedIdsAtCursorTimestamp.Clear();
            _processedThrough = null;
            _nextPruneProbeStartIndex = 0;
            _nextDeferredRetryStartIndex = 0;
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

        var deferredRetryBudget = GetDeferredRetryCount() > 0
            ? Math.Min(1, batchSize)
            : 0;
        var deferredRetryResult = deferredRetryBudget > 0
            ? await ProcessDeferredRetriesAsync(deferredRetryBudget, cancellationToken)
            : new BackfillBatchResult(Processed: 0, Failed: 0, Remaining: 0);

        var toProcess = (await _chunkRepository.GetUnindexedBatchAsync(
            GetProcessedThrough(),
            batchSize,
            cancellationToken)).ToList();

        if (toProcess.Count == 0)
        {
            var tailRetryResult = await ProcessDeferredRetriesAsync(batchSize, cancellationToken);
            return CombineResults(deferredRetryResult, tailRetryResult);
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
            AdvanceProcessedThroughVisitedPrefix(toProcess, completedChunkIds);
            var emptyRemaining = await CountRemainingAsync(cancellationToken);
            return new BackfillBatchResult(
                deferredRetryResult.Processed,
                deferredRetryResult.Failed,
                emptyRemaining);
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
            return await ProcessOneByOneAsync(
                embeddable,
                toProcess,
                completedChunkIds,
                documentMap,
                deferredRetryResult.Processed,
                deferredRetryResult.Failed,
                cancellationToken);
        }

        int processed = deferredRetryResult.Processed;
        int failed = deferredRetryResult.Failed;

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
                completedChunkIds.Add(chunk.Id);
                DeferRetry(chunk.Id);
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
                processed += vectorDocs.Count;
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
                        if (chunkIdsByDocId.TryGetValue(doc.DocumentId, out var failedChunkId))
                        {
                            completedChunkIds.Add(failedChunkId);
                            DeferRetry(failedChunkId);
                        }
                        _logger.LogWarning(
                            "Individual upsert failed for {DocId}: {Error}",
                            doc.DocumentId, upsertEx.Message);
                    }
                }
            }
        }

        AdvanceProcessedThroughVisitedPrefix(toProcess, completedChunkIds);
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
        var trackedIds = GetPruneProbeIds();

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
            "Pruned {Count} stale vectors from the index after probing {ProbeCount} tracked chunks",
            staleKeys.Count,
            trackedIds.Count);
    }

    /// <summary>
    /// Fallback: process chunks one-by-one when batch embedding fails.
    /// </summary>
    private async Task<BackfillBatchResult> ProcessOneByOneAsync(
        List<(Domain.Entities.KnowledgeChunk Chunk, string Text)> embeddable,
        IReadOnlyList<Domain.Entities.KnowledgeChunk> batchChunks,
        HashSet<Guid> completedChunkIds,
        Dictionary<Guid, Domain.Entities.KnowledgeDocument> documentMap,
        int initialProcessed,
        int initialFailed,
        CancellationToken cancellationToken)
    {
        int processed = initialProcessed;
        int failed = initialFailed;

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
                completedChunkIds.Add(chunk.Id);
                DeferRetry(chunk.Id);
                _logger.LogWarning(
                    "Failed to embed chunk {ChunkId}: {Error}",
                    chunk.Id,
                    ex.Message);
            }
        }

        AdvanceProcessedThroughVisitedPrefix(batchChunks, completedChunkIds);
        var remaining = await CountRemainingAsync(cancellationToken);

        _logger.LogInformation(
            "Embedding backfill batch complete (one-by-one fallback): {Processed} processed, {Failed} failed, ~{Remaining} remaining",
            processed, failed, remaining);

        return new BackfillBatchResult(processed, failed, remaining);
    }

    private async Task<int> CountRemainingAsync(CancellationToken cancellationToken)
    {
        var remainingAfterCursor = await _chunkRepository.CountUnindexedAsync(
            GetProcessedThrough(),
            cancellationToken);
        return remainingAfterCursor + GetDeferredRetryCount();
    }

    private static KnowledgeChunkBackfillCursor? GetProcessedThrough()
    {
        lock (_indexedLock)
        {
            if (_processedThrough is null)
                return null;

            return _processedThrough with
            {
                ProcessedIdsAtCreatedAt = _processedIdsAtCursorTimestamp.ToHashSet()
            };
        }
    }

    private static void TrackIndexedChunks(IEnumerable<Guid> chunkIds)
    {
        lock (_indexedLock)
        {
            foreach (var chunkId in chunkIds)
            {
                _indexedChunkIds.Add(chunkId);
                _deferredRetryChunkIds.Remove(chunkId);
            }
        }
    }

    private static void DeferRetry(Guid chunkId)
    {
        lock (_indexedLock)
        {
            if (!_indexedChunkIds.Contains(chunkId))
                _deferredRetryChunkIds.Add(chunkId);
        }
    }

    private static void RemoveDeferredRetries(IEnumerable<Guid> chunkIds)
    {
        lock (_indexedLock)
        {
            foreach (var chunkId in chunkIds)
                _deferredRetryChunkIds.Remove(chunkId);
        }
    }

    private static int GetDeferredRetryCount()
    {
        lock (_indexedLock)
        {
            return _deferredRetryChunkIds.Count;
        }
    }

    private static IReadOnlyList<Guid> GetDeferredRetryIds(int batchSize)
    {
        lock (_indexedLock)
        {
            if (_deferredRetryChunkIds.Count == 0 || batchSize <= 0)
                return Array.Empty<Guid>();

            var ids = _deferredRetryChunkIds.OrderBy(id => id).ToList();
            var take = Math.Min(batchSize, ids.Count);
            if (ids.Count <= take)
            {
                _nextDeferredRetryStartIndex = 0;
                return ids;
            }

            var start = _nextDeferredRetryStartIndex % ids.Count;
            var selected = new List<Guid>(take);
            for (var i = 0; i < take; i++)
            {
                selected.Add(ids[(start + i) % ids.Count]);
            }

            _nextDeferredRetryStartIndex = (start + take) % ids.Count;
            return selected;
        }
    }

    private static IReadOnlyList<Guid> GetPruneProbeIds()
    {
        lock (_indexedLock)
        {
            if (_indexedChunkIds.Count == 0)
                return Array.Empty<Guid>();

            var ids = _indexedChunkIds.OrderBy(id => id).ToList();
            if (ids.Count <= MaxStalePruneProbeCount)
            {
                _nextPruneProbeStartIndex = 0;
                return ids;
            }

            var start = _nextPruneProbeStartIndex % ids.Count;
            var selected = new List<Guid>(MaxStalePruneProbeCount);
            for (var i = 0; i < MaxStalePruneProbeCount; i++)
            {
                selected.Add(ids[(start + i) % ids.Count]);
            }

            _nextPruneProbeStartIndex = (start + selected.Count) % ids.Count;
            return selected;
        }
    }

    private async Task<BackfillBatchResult> ProcessDeferredRetriesAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        var retryIds = GetDeferredRetryIds(batchSize);
        if (retryIds.Count == 0)
            return new BackfillBatchResult(Processed: 0, Failed: 0, Remaining: 0);

        var retryChunks = new List<Domain.Entities.KnowledgeChunk>();
        foreach (var retryId in retryIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunk = await _chunkRepository.GetByIdAsync(retryId, cancellationToken);
            if (chunk is null)
            {
                RemoveDeferredRetries(new[] { retryId });
                continue;
            }

            retryChunks.Add(chunk);
        }

        if (retryChunks.Count == 0)
        {
            return new BackfillBatchResult(Processed: 0, Failed: 0, Remaining: GetDeferredRetryCount());
        }

        _logger.LogInformation(
            "Retrying {Count} deferred embedding chunks after forward backfill reached the current tail",
            retryChunks.Count);

        return await ProcessChunksWithoutAdvancingCursorAsync(retryChunks, cancellationToken);
    }

    private async Task<BackfillBatchResult> ProcessChunksWithoutAdvancingCursorAsync(
        IReadOnlyList<Domain.Entities.KnowledgeChunk> chunks,
        CancellationToken cancellationToken)
    {
        var documentIds = chunks.Select(c => c.DocumentId).Distinct().ToList();
        var documentMap = new Dictionary<Guid, Domain.Entities.KnowledgeDocument>();
        foreach (var docId in documentIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var doc = await _documentRepository.GetByIdAsync(docId, cancellationToken);
            if (doc is not null)
                documentMap[docId] = doc;
        }

        var completedChunkIds = new HashSet<Guid>();
        var embeddable = new List<(Domain.Entities.KnowledgeChunk Chunk, string Text)>();
        foreach (var chunk in chunks)
        {
            if (string.IsNullOrWhiteSpace(chunk.Content))
            {
                completedChunkIds.Add(chunk.Id);
                continue;
            }

            embeddable.Add((chunk, chunk.Content));
        }

        if (embeddable.Count == 0)
        {
            RemoveDeferredRetries(completedChunkIds);
            return new BackfillBatchResult(Processed: 0, Failed: 0, Remaining: GetDeferredRetryCount());
        }

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
                completedChunkIds.Add(chunk.Id);
                TrackIndexedChunks(new[] { chunk.Id });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                DeferRetry(chunk.Id);
                _logger.LogWarning(
                    "Deferred retry failed for chunk {ChunkId}: {Error}",
                    chunk.Id,
                    ex.Message);
            }
        }

        RemoveDeferredRetries(completedChunkIds);
        return new BackfillBatchResult(processed, failed, await CountRemainingAsync(cancellationToken));
    }

    private static BackfillBatchResult CombineResults(
        BackfillBatchResult first,
        BackfillBatchResult second)
    {
        return new BackfillBatchResult(
            first.Processed + second.Processed,
            first.Failed + second.Failed,
            second.Remaining);
    }

    private static void AdvanceProcessedThroughVisitedPrefix(
        IReadOnlyList<Domain.Entities.KnowledgeChunk> chunks,
        IReadOnlySet<Guid> visitedChunkIds)
    {
        if (chunks.Count == 0)
            return;

        lock (_indexedLock)
        {
            foreach (var chunk in chunks.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id))
            {
                if (!visitedChunkIds.Contains(chunk.Id))
                    break;

                if (_processedThrough is null || chunk.CreatedAt > _processedThrough.CreatedAt)
                {
                    _processedIdsAtCursorTimestamp.Clear();
                    _processedIdsAtCursorTimestamp.Add(chunk.Id);
                    _processedThrough = new KnowledgeChunkBackfillCursor(chunk.CreatedAt, chunk.Id);
                    continue;
                }

                if (chunk.CreatedAt == _processedThrough.CreatedAt)
                {
                    _processedIdsAtCursorTimestamp.Add(chunk.Id);
                    if (chunk.Id.CompareTo(_processedThrough.Id) > 0)
                        _processedThrough = new KnowledgeChunkBackfillCursor(chunk.CreatedAt, chunk.Id);
                }
            }
        }
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
