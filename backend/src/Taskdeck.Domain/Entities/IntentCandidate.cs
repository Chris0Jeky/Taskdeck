using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// An extracted intent from an <see cref="IntentEnvelopeV1"/>.
/// Carries a human-readable label, optional structured action type, and a
/// confidence score (0.0 -- 1.0). Linked to supporting evidence via
/// <see cref="EvidenceLink"/>.
/// </summary>
public class IntentCandidate : Entity
{
    public Guid EnvelopeId { get; private set; }

    /// <summary>
    /// Human-readable label describing the intent, e.g. "Create card for API review".
    /// </summary>
    public string Label { get; private set; } = string.Empty;

    /// <summary>
    /// Machine-readable action type, e.g. "create-card", "update-column", "archive".
    /// Null when the intent is purely informational.
    /// </summary>
    public string? ActionType { get; private set; }

    /// <summary>
    /// Confidence score in the range [0.0, 1.0].
    /// </summary>
    public double Confidence { get; private set; }

    /// <summary>
    /// Zero-based rank among sibling candidates in the same envelope.
    /// Lower rank = higher priority.
    /// </summary>
    public int Rank { get; private set; }

    private readonly List<EvidenceLink> _evidenceLinks = new();
    public IReadOnlyList<EvidenceLink> EvidenceLinks => _evidenceLinks.AsReadOnly();

    // Navigation
    public IntentEnvelopeV1 Envelope { get; private set; } = null!;

    private IntentCandidate() { } // EF Core

    public IntentCandidate(
        Guid envelopeId,
        string label,
        double confidence,
        int rank,
        string? actionType = null)
    {
        if (envelopeId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "EnvelopeId cannot be empty");
        if (string.IsNullOrWhiteSpace(label))
            throw new DomainException(ErrorCodes.ValidationError, "Label cannot be empty");
        if (label.Length > 500)
            throw new DomainException(ErrorCodes.ValidationError, "Label cannot exceed 500 characters");
        if (!double.IsFinite(confidence) || confidence < 0.0 || confidence > 1.0)
            throw new DomainException(ErrorCodes.ValidationError, "Confidence must be between 0.0 and 1.0");
        if (rank < 0)
            throw new DomainException(ErrorCodes.ValidationError, "Rank must be non-negative");
        if (actionType is not null && actionType.Length > 100)
            throw new DomainException(ErrorCodes.ValidationError, "ActionType cannot exceed 100 characters");

        EnvelopeId = envelopeId;
        Label = label;
        Confidence = confidence;
        Rank = rank;
        ActionType = string.IsNullOrWhiteSpace(actionType) ? null : actionType.Trim();
    }

    public void AddEvidenceLink(EvidenceLink link, SourceSpan sourceSpan)
    {
        if (sourceSpan is null)
            throw new DomainException(ErrorCodes.ValidationError,
                "SourceSpan is required when adding an EvidenceLink");
        if (link.IntentCandidateId != Id)
            throw new DomainException(ErrorCodes.ValidationError,
                "EvidenceLink does not belong to this IntentCandidate");
        if (link.SourceSpanId != sourceSpan.Id)
            throw new DomainException(ErrorCodes.ValidationError,
                "EvidenceLink does not point to the provided SourceSpan");
        if (sourceSpan.EnvelopeId != EnvelopeId)
            throw new DomainException(ErrorCodes.ValidationError,
                "EvidenceLink source span does not belong to this IntentCandidate envelope");

        _evidenceLinks.Add(link);
        Touch();
    }

    public void UpdateConfidence(double newConfidence)
    {
        if (!double.IsFinite(newConfidence) || newConfidence < 0.0 || newConfidence > 1.0)
            throw new DomainException(ErrorCodes.ValidationError, "Confidence must be between 0.0 and 1.0");

        Confidence = newConfidence;
        Touch();
    }
}
