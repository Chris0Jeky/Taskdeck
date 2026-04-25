using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// Immutable revision of an automation proposal. Revisions never destructively
/// overwrite the original proposal payload; they form a chronological chain.
/// Each revision captures: who edited, what the revised payload is, why, and when.
/// </summary>
public class ProposalRevision : Entity
{
    /// <summary>FK to the original AutomationProposal.</summary>
    public Guid ProposalId { get; private set; }

    /// <summary>Monotonically increasing revision number (1-based).</summary>
    public int RevisionNumber { get; private set; }

    /// <summary>The user who created this revision.</summary>
    public Guid EditorUserId { get; private set; }

    /// <summary>
    /// The revised proposal payload as JSON. This is the full snapshot of the
    /// edited operations, not a diff. The original proposal payload is preserved
    /// on the AutomationProposal entity itself.
    /// </summary>
    public string RevisedPayload { get; private set; } = string.Empty;

    /// <summary>Timestamp when the revision was created (UTC).</summary>
    public DateTimeOffset RevisedAt { get; private set; }

    /// <summary>Human-readable reason for the revision (e.g., "Updated card title").</summary>
    public string Reason { get; private set; } = string.Empty;

    // Navigation
    public AutomationProposal Proposal { get; private set; } = null!;

    private ProposalRevision() { } // EF Core

    public ProposalRevision(
        Guid proposalId,
        int revisionNumber,
        Guid editorUserId,
        string revisedPayload,
        string reason)
    {
        if (proposalId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "ProposalId cannot be empty");
        if (revisionNumber < 1)
            throw new DomainException(ErrorCodes.ValidationError, "RevisionNumber must be at least 1");
        if (editorUserId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "EditorUserId cannot be empty");
        if (string.IsNullOrWhiteSpace(revisedPayload))
            throw new DomainException(ErrorCodes.ValidationError, "RevisedPayload cannot be empty");
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException(ErrorCodes.ValidationError, "Reason cannot be empty");
        if (reason.Length > 500)
            throw new DomainException(ErrorCodes.ValidationError, "Reason cannot exceed 500 characters");

        ProposalId = proposalId;
        RevisionNumber = revisionNumber;
        EditorUserId = editorUserId;
        RevisedPayload = revisedPayload;
        Reason = reason;
        RevisedAt = DateTimeOffset.UtcNow;
    }
}
