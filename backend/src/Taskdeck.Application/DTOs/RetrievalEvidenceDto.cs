namespace Taskdeck.Application.DTOs;

/// <summary>
/// An evidence reference produced by retrieval, suitable for attaching
/// to proposals and chat context as an <c>EvidenceLink</c> reason chip.
/// </summary>
public sealed record RetrievalEvidenceDto(
    /// <summary>Source document/entity identifier.</summary>
    Guid SourceId,

    /// <summary>
    /// Source type for evidence link attribution, e.g. "knowledge_document",
    /// "capture", "card".
    /// </summary>
    string SourceType,

    /// <summary>Human-readable label for the evidence, e.g. document title.</summary>
    string Label,

    /// <summary>Relevance score in [0.0, 1.0].</summary>
    double Relevance,

    /// <summary>
    /// Rationale explaining why this evidence supports the context,
    /// e.g. "retrieved via hybrid search with RRF score 0.82".
    /// </summary>
    string Rationale);
