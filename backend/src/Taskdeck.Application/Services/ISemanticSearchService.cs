using Taskdeck.Application.DTOs;

namespace Taskdeck.Application.Services;

/// <summary>
/// Unified semantic search that uses vector nearest-neighbor search when
/// available and falls back to FTS when vector dependencies are unavailable.
/// </summary>
public interface ISemanticSearchService
{
    /// <summary>
    /// Whether vector search is currently available. When false, all queries
    /// transparently fall back to FTS.
    /// </summary>
    bool IsVectorSearchAvailable { get; }

    /// <summary>
    /// Searches knowledge using the best available method (vector or FTS).
    /// </summary>
    Task<IEnumerable<KnowledgeSearchResultDto>> SearchAsync(
        string query,
        Guid userId,
        Guid? boardId = null,
        int limit = 20,
        CancellationToken cancellationToken = default);
}
