using Microsoft.Extensions.Logging;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;

namespace Taskdeck.Application.Services;

/// <summary>
/// Combines FTS5 BM25 results and vector cosine results using Reciprocal Rank
/// Fusion (RRF). The RRF formula is: score(d) = sum over lists L of 1/(k + rank_L(d))
/// where k is a smoothing constant (default 60, per the original RRF paper).
///
/// Falls back gracefully to FTS-only when the embedding generator reports
/// unavailable, preserving the existing FTS search path.
/// </summary>
public sealed class HybridRetrievalService : IHybridRetrievalService
{
    /// <summary>
    /// RRF smoothing constant. k=60 is the value from the original Cormack et al.
    /// paper. Higher values reduce the influence of top-ranked documents.
    /// </summary>
    internal const int RrfK = 60;

    /// <summary>
    /// Over-fetch multiplier: request more results from each source than
    /// the final limit to ensure adequate coverage after fusion and dedup.
    /// </summary>
    private const int OverFetchMultiplier = 3;

    private readonly IFtsKnowledgeSearchService _ftsService;
    private readonly ISemanticSearchService _semanticSearchService;
    private readonly IEmbeddingGenerator _embeddingGenerator;
    private readonly IVectorIndex _vectorIndex;
    private readonly IKnowledgeDocumentRepository _documentRepository;
    private readonly ILogger<HybridRetrievalService> _logger;

    public HybridRetrievalService(
        IFtsKnowledgeSearchService ftsService,
        ISemanticSearchService semanticSearchService,
        IEmbeddingGenerator embeddingGenerator,
        IVectorIndex vectorIndex,
        IKnowledgeDocumentRepository documentRepository,
        ILogger<HybridRetrievalService> logger)
    {
        _ftsService = ftsService;
        _semanticSearchService = semanticSearchService;
        _embeddingGenerator = embeddingGenerator;
        _vectorIndex = vectorIndex;
        _documentRepository = documentRepository;
        _logger = logger;
    }

    public bool IsHybridAvailable => _embeddingGenerator.IsAvailable;

    public async Task<IReadOnlyList<RetrievalResultDto>> SearchAsync(
        string query,
        Guid userId,
        Guid? boardId = null,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<RetrievalResultDto>();

        if (limit <= 0)
            return Array.Empty<RetrievalResultDto>();

        var overFetchLimit = Math.Min(limit * OverFetchMultiplier, 100);

        if (!IsHybridAvailable)
        {
            _logger.LogDebug("Vector search unavailable, using FTS-only retrieval");
            return await FtsOnlySearchAsync(query, userId, boardId, overFetchLimit, limit, cancellationToken);
        }

        try
        {
            return await HybridSearchAsync(query, userId, boardId, overFetchLimit, limit, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Hybrid search failed, falling back to FTS-only: {Error}",
                ex.Message);
            return await FtsOnlySearchAsync(query, userId, boardId, overFetchLimit, limit, cancellationToken);
        }
    }

    public IReadOnlyList<RetrievalEvidenceDto> BuildEvidenceLinks(
        IReadOnlyList<RetrievalResultDto> retrievalResults)
    {
        ArgumentNullException.ThrowIfNull(retrievalResults);

        var evidence = new List<RetrievalEvidenceDto>(retrievalResults.Count);
        foreach (var result in retrievalResults)
        {
            // Normalize score to [0.0, 1.0] for relevance
            var relevance = Math.Clamp(result.Score, 0.0, 1.0);

            var sourceType = "knowledge_document";
            var rationale = result.Source switch
            {
                RetrievalSource.Fts => $"retrieved via full-text search (score {result.Score:F3})",
                RetrievalSource.Vector => $"retrieved via vector similarity (score {result.Score:F3})",
                RetrievalSource.Hybrid => $"retrieved via hybrid search with RRF (score {result.Score:F3})",
                _ => $"retrieved (score {result.Score:F3})"
            };

            evidence.Add(new RetrievalEvidenceDto(
                SourceId: result.DocumentId,
                SourceType: sourceType,
                Label: result.Title,
                Relevance: relevance,
                Rationale: rationale));
        }

        return evidence;
    }

    private async Task<IReadOnlyList<RetrievalResultDto>> HybridSearchAsync(
        string query,
        Guid userId,
        Guid? boardId,
        int overFetchLimit,
        int limit,
        CancellationToken cancellationToken)
    {
        // Run FTS and vector search in parallel
        var ftsTask = SafeFtsSearchAsync(query, userId, boardId, overFetchLimit, cancellationToken);
        var vectorTask = SafeVectorSearchAsync(query, userId, boardId, overFetchLimit, cancellationToken);

        await Task.WhenAll(ftsTask, vectorTask);

        var ftsResults = await ftsTask;
        var vectorResults = await vectorTask;

        // If both are empty, return empty
        if (ftsResults.Count == 0 && vectorResults.Count == 0)
            return Array.Empty<RetrievalResultDto>();

        // If only one source returned results, use it directly
        if (vectorResults.Count == 0)
        {
            _logger.LogDebug("Vector returned no results, using FTS-only");
            return ftsResults.Take(limit).ToList();
        }

        if (ftsResults.Count == 0)
        {
            _logger.LogDebug("FTS returned no results, using vector-only");
            return vectorResults.Take(limit).ToList();
        }

        // Apply Reciprocal Rank Fusion
        var fused = ApplyRrf(ftsResults, vectorResults);

        return fused.Take(limit).ToList();
    }

    /// <summary>
    /// Applies Reciprocal Rank Fusion to merge two ranked result lists.
    /// RRF score(d) = sum over lists L of 1/(k + rank_L(d))
    /// where rank is 1-based position in each list.
    /// </summary>
    internal static IReadOnlyList<RetrievalResultDto> ApplyRrf(
        IReadOnlyList<RetrievalResultDto> ftsResults,
        IReadOnlyList<RetrievalResultDto> vectorResults)
    {
        var rrfScores = new Dictionary<Guid, (double Score, RetrievalResultDto BestResult)>();

        // Score FTS results
        for (int i = 0; i < ftsResults.Count; i++)
        {
            var result = ftsResults[i];
            var rrfScore = 1.0 / (RrfK + i + 1); // rank is 1-based
            if (rrfScores.TryGetValue(result.DocumentId, out var existing))
            {
                rrfScores[result.DocumentId] = (existing.Score + rrfScore, existing.BestResult);
            }
            else
            {
                rrfScores[result.DocumentId] = (rrfScore, result);
            }
        }

        // Score vector results
        for (int i = 0; i < vectorResults.Count; i++)
        {
            var result = vectorResults[i];
            var rrfScore = 1.0 / (RrfK + i + 1);
            if (rrfScores.TryGetValue(result.DocumentId, out var existing))
            {
                rrfScores[result.DocumentId] = (existing.Score + rrfScore, existing.BestResult);
            }
            else
            {
                rrfScores[result.DocumentId] = (rrfScore, result);
            }
        }

        // Build fused results sorted by RRF score descending
        var fused = rrfScores
            .OrderByDescending(kvp => kvp.Value.Score)
            .Select(kvp => kvp.Value.BestResult with
            {
                Score = kvp.Value.Score,
                Source = RetrievalSource.Hybrid
            })
            .ToList();

        return fused;
    }

    private async Task<IReadOnlyList<RetrievalResultDto>> SafeFtsSearchAsync(
        string query,
        Guid userId,
        Guid? boardId,
        int limit,
        CancellationToken cancellationToken)
    {
        try
        {
            var ftsResults = await _ftsService.SearchAsync(
                query, userId, boardId, limit, cancellationToken);

            return ftsResults.Select(r => new RetrievalResultDto(
                DocumentId: r.DocumentId,
                Title: r.Title,
                Snippet: r.Snippet,
                Score: r.Rank,
                BoardId: r.BoardId,
                Source: RetrievalSource.Fts,
                Tags: r.Tags,
                CreatedAt: r.CreatedAt)).ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("FTS search failed in hybrid retrieval: {Error}", ex.Message);
            return Array.Empty<RetrievalResultDto>();
        }
    }

    private async Task<IReadOnlyList<RetrievalResultDto>> SafeVectorSearchAsync(
        string query,
        Guid userId,
        Guid? boardId,
        int limit,
        CancellationToken cancellationToken)
    {
        try
        {
            var queryVector = await _embeddingGenerator.GenerateAsync(query, cancellationToken);

            var filter = new Dictionary<string, string>
            {
                ["type"] = "knowledge_chunk",
                ["userId"] = userId.ToString()
            };

            if (boardId.HasValue)
                filter["boardId"] = boardId.Value.ToString();

            var vectorResults = await _vectorIndex.QueryAsync(
                queryVector,
                topK: limit,
                filter: filter,
                cancellationToken: cancellationToken);

            // Hydrate vector results with document data
            var results = new List<RetrievalResultDto>();
            var seenDocIds = new HashSet<Guid>();

            foreach (var vr in vectorResults)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (vr.Metadata is null)
                    continue;

                if (!Guid.TryParse(
                        vr.Metadata.GetValueOrDefault("documentId") ?? string.Empty,
                        out var docId) || docId == Guid.Empty)
                    continue;

                if (!seenDocIds.Add(docId))
                    continue; // Dedup by document

                var doc = await _documentRepository.GetByIdAsync(docId, cancellationToken);
                if (doc is null || doc.IsArchived || doc.UserId != userId)
                    continue;

                if (boardId.HasValue && doc.BoardId != boardId)
                    continue;

                var snippet = doc.Content.Length > 200
                    ? doc.Content[..200] + "..."
                    : doc.Content;

                results.Add(new RetrievalResultDto(
                    DocumentId: docId,
                    Title: doc.Title,
                    Snippet: snippet,
                    Score: vr.Score,
                    BoardId: doc.BoardId,
                    Source: RetrievalSource.Vector,
                    Tags: doc.Tags,
                    CreatedAt: doc.CreatedAt));
            }

            return results;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Vector search failed in hybrid retrieval: {Error}", ex.Message);
            return Array.Empty<RetrievalResultDto>();
        }
    }

    private async Task<IReadOnlyList<RetrievalResultDto>> FtsOnlySearchAsync(
        string query,
        Guid userId,
        Guid? boardId,
        int overFetchLimit,
        int limit,
        CancellationToken cancellationToken)
    {
        var ftsResults = await _ftsService.SearchAsync(
            query, userId, boardId, overFetchLimit, cancellationToken);

        return ftsResults.Select(r => new RetrievalResultDto(
            DocumentId: r.DocumentId,
            Title: r.Title,
            Snippet: r.Snippet,
            Score: r.Rank,
            BoardId: r.BoardId,
            Source: RetrievalSource.Fts,
            Tags: r.Tags,
            CreatedAt: r.CreatedAt))
            .Take(limit)
            .ToList();
    }
}
