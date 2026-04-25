using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// A structured reference linking a provenance field to its source material.
/// Examples: a specific inbox capture, a chat message, a document chunk.
/// </summary>
public class EvidenceLink : Entity
{
    /// <summary>
    /// The type of source (e.g., "InboxCapture", "ChatMessage", "KnowledgeChunk").
    /// </summary>
    public string SourceType { get; private set; } = string.Empty;

    /// <summary>
    /// Identifier of the source entity (e.g., the capture or message ID).
    /// </summary>
    public string SourceId { get; private set; } = string.Empty;

    /// <summary>
    /// Optional human-readable label for the evidence source.
    /// </summary>
    public string? Label { get; private set; }

    /// <summary>
    /// Optional character offset into the source text where the relevant span begins.
    /// </summary>
    public int? SpanStart { get; private set; }

    /// <summary>
    /// Optional character offset into the source text where the relevant span ends.
    /// </summary>
    public int? SpanEnd { get; private set; }

    /// <summary>
    /// FK to the parent ProvenanceField.
    /// </summary>
    public Guid ProvenanceFieldId { get; private set; }

    private EvidenceLink() { } // EF Core

    public EvidenceLink(
        string sourceType,
        string sourceId,
        Guid provenanceFieldId,
        string? label = null,
        int? spanStart = null,
        int? spanEnd = null)
    {
        if (string.IsNullOrWhiteSpace(sourceType))
            throw new DomainException(ErrorCodes.ValidationError, "SourceType cannot be empty");
        if (sourceType.Length > 100)
            throw new DomainException(ErrorCodes.ValidationError, "SourceType cannot exceed 100 characters");
        if (string.IsNullOrWhiteSpace(sourceId))
            throw new DomainException(ErrorCodes.ValidationError, "SourceId cannot be empty");
        if (sourceId.Length > 500)
            throw new DomainException(ErrorCodes.ValidationError, "SourceId cannot exceed 500 characters");
        if (provenanceFieldId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "ProvenanceFieldId cannot be empty");
        if (label != null && label.Length > 200)
            throw new DomainException(ErrorCodes.ValidationError, "Label cannot exceed 200 characters");
        if (spanStart.HasValue && spanStart.Value < 0)
            throw new DomainException(ErrorCodes.ValidationError, "SpanStart cannot be negative");
        if (spanEnd.HasValue && spanEnd.Value < 0)
            throw new DomainException(ErrorCodes.ValidationError, "SpanEnd cannot be negative");
        if (spanStart.HasValue && spanEnd.HasValue && spanEnd.Value < spanStart.Value)
            throw new DomainException(ErrorCodes.ValidationError, "SpanEnd cannot be less than SpanStart");

        SourceType = sourceType;
        SourceId = sourceId;
        ProvenanceFieldId = provenanceFieldId;
        Label = label;
        SpanStart = spanStart;
        SpanEnd = spanEnd;
    }
}
