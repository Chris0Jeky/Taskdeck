using Taskdeck.Application.DTOs;

namespace Taskdeck.Application.Services;

/// <summary>
/// Combines FTS5 BM25 and vector cosine results using Reciprocal Rank Fusion (RRF).
/// Falls back to FTS-only when vector search is unavailable.
/// </summary>
public interface IHybridRetrievalService
{
    /// <summary>
    /// Whether hybrid (FTS + vector) retrieval is available.
    /// When false, all queries use FTS-only.
    /// </summary>
    bool IsHybridAvailable { get; }

    /// <summary>
    /// Retrieves documents using Reciprocal Rank Fusion over FTS and vector results.
    /// </summary>
    Task<IReadOnlyList<RetrievalResultDto>> SearchAsync(
        string query,
        Guid userId,
        Guid? boardId = null,
        int limit = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts retrieval results into evidence references suitable for
    /// attaching to proposals and chat context.
    /// </summary>
    IReadOnlyList<RetrievalEvidenceDto> BuildEvidenceLinks(
        IReadOnlyList<RetrievalResultDto> retrievalResults);
}
