namespace Taskdeck.Application.DTOs;

/// <summary>
/// A source-agnostic retrieval result used as the common currency across
/// FTS, vector, and hybrid search paths. Each result carries a normalized
/// score so that Reciprocal Rank Fusion can merge heterogeneous result sets.
/// </summary>
public sealed record RetrievalResultDto(
    /// <summary>Document or entity identifier.</summary>
    Guid DocumentId,

    /// <summary>Human-readable title or label for the retrieved item.</summary>
    string Title,

    /// <summary>Short excerpt or snippet providing context.</summary>
    string Snippet,

    /// <summary>
    /// Fused or normalized relevance score. Higher is better.
    /// For RRF this is the sum of 1/(k+rank) across contributing lists.
    /// </summary>
    double Score,

    /// <summary>Board association, if any.</summary>
    Guid? BoardId,

    /// <summary>Provenance: which retrieval path produced this result.</summary>
    RetrievalSource Source,

    /// <summary>Optional tags from the source document.</summary>
    string? Tags = null,

    /// <summary>When the source document was created.</summary>
    DateTimeOffset? CreatedAt = null);

/// <summary>
/// Identifies which retrieval method produced a result, used for
/// provenance attribution in evidence-linked proposals.
/// </summary>
public enum RetrievalSource
{
    /// <summary>Full-text search (FTS5 BM25).</summary>
    Fts,

    /// <summary>Vector nearest-neighbor (cosine similarity).</summary>
    Vector,

    /// <summary>Reciprocal Rank Fusion of FTS + vector.</summary>
    Hybrid
}
