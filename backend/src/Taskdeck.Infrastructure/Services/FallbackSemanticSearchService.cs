using Microsoft.Extensions.Logging;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;
using Taskdeck.Application.Services;

namespace Taskdeck.Infrastructure.Services;

/// <summary>
/// Semantic search service that uses vector nearest-neighbor search when
/// the embedding generator and vector index are both available, and
/// transparently falls back to FTS-only search otherwise.
///
/// This preserves the existing FTS behavior for deployments that do not
/// have vector dependencies installed or configured.
/// </summary>
public sealed class FallbackSemanticSearchService : ISemanticSearchService
{
    private readonly IVectorIndex _vectorIndex;
    private readonly IEmbeddingGenerator _embeddingGenerator;
    private readonly IKnowledgeSearchService _ftsSearchService;
    private readonly IKnowledgeDocumentRepository _documentRepository;
    private readonly ILogger<FallbackSemanticSearchService> _logger;

    public FallbackSemanticSearchService(
        IVectorIndex vectorIndex,
        IEmbeddingGenerator embeddingGenerator,
        IKnowledgeSearchService ftsSearchService,
        IKnowledgeDocumentRepository documentRepository,
        ILogger<FallbackSemanticSearchService> logger)
    {
        _vectorIndex = vectorIndex;
        _embeddingGenerator = embeddingGenerator;
        _ftsSearchService = ftsSearchService;
        _documentRepository = documentRepository;
        _logger = logger;
    }

    public bool IsVectorSearchAvailable => _embeddingGenerator.IsAvailable;

    public async Task<IEnumerable<KnowledgeSearchResultDto>> SearchAsync(
        string query,
        Guid userId,
        Guid? boardId = null,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Enumerable.Empty<KnowledgeSearchResultDto>();

        if (!IsVectorSearchAvailable)
        {
            _logger.LogDebug("Vector search unavailable, falling back to FTS");
            return await _ftsSearchService.SearchAsync(
                query, userId, boardId, limit, cancellationToken);
        }

        try
        {
            return await VectorSearchAsync(query, userId, boardId, limit, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Vector search failed, falling back to FTS. Error: {Error}",
                ex.Message);

            return await _ftsSearchService.SearchAsync(
                query, userId, boardId, limit, cancellationToken);
        }
    }

    private async Task<IEnumerable<KnowledgeSearchResultDto>> VectorSearchAsync(
        string query,
        Guid userId,
        Guid? boardId,
        int limit,
        CancellationToken cancellationToken)
    {
        var queryVector = await _embeddingGenerator.GenerateAsync(query, cancellationToken);

        // Filter by type and userId for access control
        var filter = new Dictionary<string, string>
        {
            ["type"] = "knowledge_chunk",
            ["userId"] = userId.ToString()
        };

        // When a boardId is specified, further restrict to that board
        if (boardId.HasValue)
        {
            filter["boardId"] = boardId.Value.ToString();
        }

        // Request more results than needed to account for post-filtering
        var vectorResults = await _vectorIndex.QueryAsync(
            queryVector,
            topK: limit * 2,
            filter: filter,
            cancellationToken: cancellationToken);

        if (vectorResults.Count == 0)
        {
            _logger.LogDebug(
                "Vector search returned no results for query, falling back to FTS");
            return await _ftsSearchService.SearchAsync(
                query, userId, boardId, limit, cancellationToken);
        }

        // Hydrate results with document metadata (Title, Snippet, etc.)
        var results = new List<KnowledgeSearchResultDto>();
        foreach (var r in vectorResults.Take(limit))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (r.Metadata is null)
                continue;

            if (!Guid.TryParse(
                    r.Metadata.GetValueOrDefault("documentId") ?? string.Empty,
                    out var docId) || docId == Guid.Empty)
            {
                _logger.LogDebug("Skipping vector result with unparseable documentId");
                continue;
            }

            var doc = await _documentRepository.GetByIdAsync(docId, cancellationToken);
            if (doc is null)
                continue;

            // Double-check access control at the document level
            if (doc.UserId != userId)
                continue;

            if (boardId.HasValue && doc.BoardId != boardId)
                continue;

            var snippet = doc.Content.Length > 200
                ? doc.Content[..200] + "..."
                : doc.Content;

            results.Add(new KnowledgeSearchResultDto(
                DocumentId: docId,
                Title: doc.Title,
                Snippet: snippet,
                Rank: r.Score,
                BoardId: doc.BoardId,
                SourceType: doc.SourceType,
                Tags: doc.Tags,
                CreatedAt: doc.CreatedAt));
        }

        if (results.Count == 0)
        {
            _logger.LogDebug(
                "Vector search yielded no hydrated results, falling back to FTS");
            return await _ftsSearchService.SearchAsync(
                query, userId, boardId, limit, cancellationToken);
        }

        return results;
    }
}
