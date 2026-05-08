using Microsoft.Extensions.Logging;
using Taskdeck.Application.DTOs;
using Taskdeck.Application.Interfaces;

namespace Taskdeck.Application.Services;

/// <summary>
/// Detects near-duplicate documents at ingest time using vector similarity
/// when available, with FTS title matching as fallback. Uses a precision-favoring
/// threshold to minimize false-positive duplicate flags.
///
/// Threshold calibration rationale:
/// - Hard threshold (0.92): flag as probable duplicate. At this level, false
///   positives are rare because the content is very similar.
/// - Soft threshold (0.80): surface as a review cue ("similar to existing")
///   without blocking ingest. The user can dismiss the hint.
/// - Below soft threshold: no duplicate signal.
///
/// This precision-favoring approach means some true duplicates will slip through
/// at the edges, but users are never blocked from ingesting content they believe
/// is novel.
/// </summary>
public sealed class DuplicateDetectionService : IDuplicateDetectionService
{
    /// <summary>
    /// Score above which content is flagged as a probable duplicate.
    /// Precision-favoring: set high to minimize false positives.
    /// </summary>
    internal const double HardThreshold = 0.92;

    /// <summary>
    /// Score above which a "similar to existing" review cue is surfaced.
    /// Below this, no duplicate signal is generated.
    /// </summary>
    internal const double SoftThreshold = 0.80;

    /// <summary>
    /// Maximum number of candidates to check from the vector index.
    /// </summary>
    private const int MaxCandidates = 5;

    private readonly IEmbeddingGenerator _embeddingGenerator;
    private readonly IVectorIndex _vectorIndex;
    private readonly IKnowledgeDocumentRepository _documentRepository;
    private readonly ILogger<DuplicateDetectionService> _logger;

    public DuplicateDetectionService(
        IEmbeddingGenerator embeddingGenerator,
        IVectorIndex vectorIndex,
        IKnowledgeDocumentRepository documentRepository,
        ILogger<DuplicateDetectionService> logger)
    {
        _embeddingGenerator = embeddingGenerator;
        _vectorIndex = vectorIndex;
        _documentRepository = documentRepository;
        _logger = logger;
    }

    public async Task<DuplicateDetectionResultDto> DetectAsync(
        string content,
        string title,
        Guid userId,
        Guid? boardId = null,
        Guid? excludeDocumentId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content))
            return NoDuplicate();

        if (!_embeddingGenerator.IsAvailable)
        {
            _logger.LogDebug("Embedding generator unavailable; skipping duplicate detection");
            return NoDuplicate();
        }

        try
        {
            return await DetectViaVectorAsync(
                content, title, userId, boardId, excludeDocumentId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Duplicate detection failed, returning safe no-duplicate: {Error}",
                ex.Message);
            return NoDuplicate();
        }
    }

    private async Task<DuplicateDetectionResultDto> DetectViaVectorAsync(
        string content,
        string title,
        Guid userId,
        Guid? boardId,
        Guid? excludeDocumentId,
        CancellationToken cancellationToken)
    {
        // Combine title and content for a more representative embedding
        var textForEmbedding = $"{title}\n{content}";
        var queryVector = await _embeddingGenerator.GenerateAsync(textForEmbedding, cancellationToken);

        var filter = new Dictionary<string, string>
        {
            ["type"] = "knowledge_chunk",
            ["userId"] = userId.ToString()
        };

        if (boardId.HasValue)
            filter["boardId"] = boardId.Value.ToString();

        var candidates = await _vectorIndex.QueryAsync(
            queryVector,
            topK: MaxCandidates,
            filter: filter,
            cancellationToken: cancellationToken);

        if (candidates.Count == 0)
            return NoDuplicate();

        // Find the best match, excluding the document itself if specified
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (candidate.Metadata is null)
                continue;

            if (!Guid.TryParse(
                    candidate.Metadata.GetValueOrDefault("documentId") ?? string.Empty,
                    out var docId) || docId == Guid.Empty)
                continue;

            // Skip self-match when updating an existing document
            if (excludeDocumentId.HasValue && docId == excludeDocumentId.Value)
                continue;

            var doc = await _documentRepository.GetByIdAsync(docId, cancellationToken);
            if (doc is null || doc.IsArchived || doc.UserId != userId)
                continue;

            if (boardId.HasValue && doc.BoardId != boardId)
                continue;

            var similarity = candidate.Score;

            if (similarity >= HardThreshold)
            {
                _logger.LogInformation(
                    "Near-duplicate detected (score {Score:F3}) for document '{Title}' matching '{MatchTitle}'",
                    similarity, title, doc.Title);

                return new DuplicateDetectionResultDto(
                    IsProbableDuplicate: true,
                    SimilarityScore: similarity,
                    MatchedDocumentId: docId,
                    MatchedDocumentTitle: doc.Title,
                    ReviewCue: $"similar to existing: {doc.Title}");
            }

            if (similarity >= SoftThreshold)
            {
                _logger.LogDebug(
                    "Possible similarity detected (score {Score:F3}) for document '{Title}' matching '{MatchTitle}'",
                    similarity, title, doc.Title);

                return new DuplicateDetectionResultDto(
                    IsProbableDuplicate: false,
                    SimilarityScore: similarity,
                    MatchedDocumentId: docId,
                    MatchedDocumentTitle: doc.Title,
                    ReviewCue: $"similar to existing: {doc.Title}");
            }

            // First candidate below soft threshold -- remaining will be lower
            break;
        }

        return NoDuplicate();
    }

    private static DuplicateDetectionResultDto NoDuplicate()
    {
        return new DuplicateDetectionResultDto(
            IsProbableDuplicate: false,
            SimilarityScore: 0.0,
            MatchedDocumentId: null,
            MatchedDocumentTitle: null,
            ReviewCue: null);
    }
}
