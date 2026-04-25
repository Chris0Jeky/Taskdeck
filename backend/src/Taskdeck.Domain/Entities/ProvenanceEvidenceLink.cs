using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// A structured reference linking a provenance field to its source material.
/// Examples: a specific inbox capture, a chat message, or a document chunk.
/// </summary>
public class ProvenanceEvidenceLink : Entity
{
    public string SourceType { get; private set; } = string.Empty;
    public string SourceId { get; private set; } = string.Empty;
    public string? Label { get; private set; }
    public int? SpanStart { get; private set; }
    public int? SpanEnd { get; private set; }
    public Guid ProvenanceFieldId { get; private set; }

    private ProvenanceEvidenceLink() { } // EF Core

    public ProvenanceEvidenceLink(
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
