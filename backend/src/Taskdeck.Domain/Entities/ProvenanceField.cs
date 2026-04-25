using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// Represents a single field of a proposal with its provenance metadata:
/// how it was derived, from what source, and with what confidence.
/// </summary>
public class ProvenanceField : Entity
{
    /// <summary>
    /// Name of the proposal field (e.g., "Title", "Description", "DueDate").
    /// </summary>
    public string FieldName { get; private set; } = string.Empty;

    /// <summary>
    /// Whether this field was extracted verbatim or inferred/synthesized.
    /// </summary>
    public ProvenanceKind Kind { get; private set; }

    /// <summary>
    /// Confidence score in the range [0.0, 1.0].
    /// For extractive fields, this reflects fuzzy match quality.
    /// For inferred fields, this reflects model confidence.
    /// </summary>
    public double Confidence { get; private set; }

    /// <summary>
    /// The extractive quote from the source (for Extractive kind).
    /// Null for inferred fields.
    /// </summary>
    public string? ExtractiveQuote { get; private set; }

    /// <summary>
    /// FK to the parent ProposalProvenance.
    /// </summary>
    public Guid ProposalProvenanceId { get; private set; }

    private readonly List<EvidenceLink> _evidenceLinks = new();
    public IReadOnlyList<EvidenceLink> EvidenceLinks => _evidenceLinks.AsReadOnly();

    private ProvenanceField() { } // EF Core

    public ProvenanceField(
        string fieldName,
        ProvenanceKind kind,
        double confidence,
        Guid proposalProvenanceId,
        string? extractiveQuote = null)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
            throw new DomainException(ErrorCodes.ValidationError, "FieldName cannot be empty");
        if (fieldName.Length > 100)
            throw new DomainException(ErrorCodes.ValidationError, "FieldName cannot exceed 100 characters");
        if (!Enum.IsDefined(kind))
            throw new DomainException(ErrorCodes.ValidationError, "ProvenanceKind value is invalid");
        if (!double.IsFinite(confidence) || confidence < 0.0 || confidence > 1.0)
            throw new DomainException(ErrorCodes.ValidationError, "Confidence must be between 0.0 and 1.0");
        if (proposalProvenanceId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "ProposalProvenanceId cannot be empty");
        if (kind == ProvenanceKind.Extractive && string.IsNullOrWhiteSpace(extractiveQuote))
            throw new DomainException(ErrorCodes.ValidationError, "ExtractiveQuote is required for Extractive provenance kind");
        if (extractiveQuote != null && extractiveQuote.Length > 2000)
            throw new DomainException(ErrorCodes.ValidationError, "ExtractiveQuote cannot exceed 2000 characters");

        FieldName = fieldName;
        Kind = kind;
        Confidence = confidence;
        ProposalProvenanceId = proposalProvenanceId;
        ExtractiveQuote = extractiveQuote;
    }

    public void AddEvidenceLink(EvidenceLink link)
    {
        if (link is null)
            throw new DomainException(ErrorCodes.ValidationError, "EvidenceLink cannot be null");
        if (link.ProvenanceFieldId != Id)
            throw new DomainException(ErrorCodes.ValidationError, "EvidenceLink does not belong to this ProvenanceField");

        _evidenceLinks.Add(link);
        Touch();
    }

    /// <summary>
    /// Downgrades the confidence value. Used when verification detects a partial match.
    /// The new confidence must be lower than the current value.
    /// </summary>
    public void DowngradeConfidence(double newConfidence)
    {
        if (!double.IsFinite(newConfidence) || newConfidence < 0.0 || newConfidence > 1.0)
            throw new DomainException(ErrorCodes.ValidationError, "Confidence must be between 0.0 and 1.0");
        if (newConfidence >= Confidence)
            throw new DomainException(ErrorCodes.InvalidOperation, "New confidence must be lower than current confidence for a downgrade");

        Confidence = newConfidence;
        Touch();
    }
}
