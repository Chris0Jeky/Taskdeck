using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// Links an <see cref="IntentCandidate"/> to a <see cref="SourceSpan"/>
/// that provides supporting evidence for that intent. Carries a relevance
/// weight so the review UI can highlight the most important evidence first.
/// </summary>
public class EvidenceLink : Entity
{
    public Guid IntentCandidateId { get; private set; }
    public Guid SourceSpanId { get; private set; }

    /// <summary>
    /// Relevance weight in the range [0.0, 1.0]. Higher values indicate
    /// stronger evidence. Defaults to 1.0 (fully relevant).
    /// </summary>
    public double Relevance { get; private set; }

    /// <summary>
    /// Optional human-readable note explaining why this span supports
    /// the linked intent (e.g. "contains deadline mention").
    /// </summary>
    public string? Rationale { get; private set; }

    // Navigation
    public IntentCandidate IntentCandidate { get; private set; } = null!;
    public SourceSpan SourceSpan { get; private set; } = null!;

    private EvidenceLink() { } // EF Core

    public EvidenceLink(
        Guid intentCandidateId,
        Guid sourceSpanId,
        double relevance = 1.0,
        string? rationale = null)
    {
        if (intentCandidateId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "IntentCandidateId cannot be empty");
        if (sourceSpanId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "SourceSpanId cannot be empty");
        if (relevance < 0.0 || relevance > 1.0)
            throw new DomainException(ErrorCodes.ValidationError, "Relevance must be between 0.0 and 1.0");
        if (rationale is not null && rationale.Length > 500)
            throw new DomainException(ErrorCodes.ValidationError, "Rationale cannot exceed 500 characters");

        IntentCandidateId = intentCandidateId;
        SourceSpanId = sourceSpanId;
        Relevance = relevance;
        Rationale = string.IsNullOrWhiteSpace(rationale) ? null : rationale.Trim();
    }

    public void UpdateRelevance(double newRelevance)
    {
        if (newRelevance < 0.0 || newRelevance > 1.0)
            throw new DomainException(ErrorCodes.ValidationError, "Relevance must be between 0.0 and 1.0");

        Relevance = newRelevance;
        Touch();
    }
}
