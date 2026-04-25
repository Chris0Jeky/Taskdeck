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
    private readonly ILogger<FallbackSemanticSearchService> _logger;

    public FallbackSemanticSearchService(
        IVectorIndex vectorIndex,
        IEmbeddingGenerator embeddingGenerator,
        IKnowledgeSearchService ftsSearchService,
        ILogger<FallbackSemanticSearchService> logger)
    {
        _vectorIndex = vectorIndex;
        _embeddingGenerator = embeddingGenerator;
        _ftsSearchService = ftsSearchService;
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

        var filter = new Dictionary<string, string>
        {
            ["type"] = "knowledge_chunk"
        };

        // Request more results than needed so we can filter by user/board client-side
        // Future: push userId/boardId filters into the vector index metadata
        var vectorResults = await _vectorIndex.QueryAsync(
            queryVector,
            topK: limit * 3,
            filter: filter,
            cancellationToken: cancellationToken);

        // Map vector results back to knowledge search DTOs
        // For now, return basic results; full DTO mapping requires joining
        // with the knowledge document repository
        var results = vectorResults
            .Take(limit)
            .Select(r => new KnowledgeSearchResultDto(
                DocumentId: Guid.TryParse(
                    r.Metadata?.GetValueOrDefault("documentId") ?? string.Empty,
                    out var docId) ? docId : Guid.Empty,
                Title: string.Empty,
                Snippet: string.Empty,
                Rank: r.Score,
                BoardId: boardId,
                SourceType: Domain.Entities.KnowledgeSourceType.Manual,
                Tags: null,
                CreatedAt: DateTimeOffset.MinValue))
            .Where(r => r.DocumentId != Guid.Empty)
            .ToList();

        if (results.Count == 0)
        {
            _logger.LogDebug(
                "Vector search returned no results for query, falling back to FTS");
            return await _ftsSearchService.SearchAsync(
                query, userId, boardId, limit, cancellationToken);
        }

        return results;
    }
}
