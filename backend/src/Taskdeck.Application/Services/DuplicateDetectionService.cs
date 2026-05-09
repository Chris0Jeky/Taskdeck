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
    private readonly IFtsKnowledgeSearchService _ftsService;
    private readonly ILogger<DuplicateDetectionService> _logger;

    public DuplicateDetectionService(
        IEmbeddingGenerator embeddingGenerator,
        IVectorIndex vectorIndex,
        IKnowledgeDocumentRepository documentRepository,
        IFtsKnowledgeSearchService ftsService,
        ILogger<DuplicateDetectionService> logger)
    {
        _embeddingGenerator = embeddingGenerator;
        _vectorIndex = vectorIndex;
        _documentRepository = documentRepository;
        _ftsService = ftsService;
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
            _logger.LogDebug(
                "Embedding generator unavailable; falling back to FTS title-based duplicate detection");
            return await DetectViaTitleFtsAsync(
                title, userId, boardId, excludeDocumentId, cancellationToken);
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
                "Vector duplicate detection failed, falling back to FTS title match: {Error}",
                ex.Message);
            return await DetectViaTitleFtsAsync(
                title, userId, boardId, excludeDocumentId, cancellationToken);
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

        // Batch fetch all candidate documents in a single query (avoids N+1)
        var candidateDocIds = candidates
            .Where(c => c.Metadata is not null)
            .Select(c => Guid.TryParse(
                c.Metadata!.GetValueOrDefault("documentId") ?? string.Empty,
                out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Where(id => !excludeDocumentId.HasValue || id != excludeDocumentId.Value)
            .Distinct()
            .ToList();

        if (candidateDocIds.Count == 0)
            return NoDuplicate();

        var documents = await _documentRepository.GetByIdsAsync(candidateDocIds, cancellationToken);
        var documentLookup = documents.ToDictionary(d => d.Id);

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

            if (!documentLookup.TryGetValue(docId, out var doc))
                continue;

            if (doc.IsArchived || doc.UserId != userId)
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

    /// <summary>
    /// FTS-based fallback for duplicate detection when vector search is unavailable.
    /// Searches by title via FTS and applies a simple normalized Levenshtein distance
    /// to detect exact or near-exact title matches. Less precise than vector similarity
    /// but ensures duplicate detection is not completely disabled in non-vector deployments.
    /// </summary>
    private async Task<DuplicateDetectionResultDto> DetectViaTitleFtsAsync(
        string title,
        Guid userId,
        Guid? boardId,
        Guid? excludeDocumentId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title))
            return NoDuplicate();

        try
        {
            var ftsResults = await _ftsService.SearchAsync(
                title, userId, boardId, MaxCandidates, cancellationToken);

            foreach (var ftsResult in ftsResults)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (excludeDocumentId.HasValue && ftsResult.DocumentId == excludeDocumentId.Value)
                    continue;

                var similarity = ComputeTitleSimilarity(title, ftsResult.Title);

                if (similarity >= HardThreshold)
                {
                    _logger.LogInformation(
                        "FTS title-match duplicate detected (similarity {Score:F3}) for '{Title}' matching '{MatchTitle}'",
                        similarity, title, ftsResult.Title);

                    return new DuplicateDetectionResultDto(
                        IsProbableDuplicate: true,
                        SimilarityScore: similarity,
                        MatchedDocumentId: ftsResult.DocumentId,
                        MatchedDocumentTitle: ftsResult.Title,
                        ReviewCue: $"similar to existing: {ftsResult.Title}");
                }

                if (similarity >= SoftThreshold)
                {
                    _logger.LogDebug(
                        "FTS title-match similarity detected (score {Score:F3}) for '{Title}' matching '{MatchTitle}'",
                        similarity, title, ftsResult.Title);

                    return new DuplicateDetectionResultDto(
                        IsProbableDuplicate: false,
                        SimilarityScore: similarity,
                        MatchedDocumentId: ftsResult.DocumentId,
                        MatchedDocumentTitle: ftsResult.Title,
                        ReviewCue: $"similar to existing: {ftsResult.Title}");
                }
            }

            return NoDuplicate();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "FTS title-based duplicate detection failed: {Error}", ex.Message);
            return NoDuplicate();
        }
    }

    /// <summary>
    /// Computes a normalized similarity score between two titles using
    /// case-insensitive comparison with Levenshtein distance. Returns 1.0 for
    /// identical titles, 0.0 for completely different ones.
    /// </summary>
    internal static double ComputeTitleSimilarity(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return 0.0;

        var aNorm = a.Trim().ToUpperInvariant();
        var bNorm = b.Trim().ToUpperInvariant();

        if (aNorm == bNorm)
            return 1.0;

        var maxLen = Math.Max(aNorm.Length, bNorm.Length);
        if (maxLen == 0)
            return 1.0;

        var distance = LevenshteinDistance(aNorm, bNorm);
        return 1.0 - ((double)distance / maxLen);
    }

    private static int LevenshteinDistance(string s, string t)
    {
        var n = s.Length;
        var m = t.Length;

        // Use single-row optimization to avoid allocating a full matrix
        var previous = new int[m + 1];
        var current = new int[m + 1];

        for (var j = 0; j <= m; j++)
            previous[j] = j;

        for (var i = 1; i <= n; i++)
        {
            current[0] = i;
            for (var j = 1; j <= m; j++)
            {
                var cost = s[i - 1] == t[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }

        return previous[m];
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
