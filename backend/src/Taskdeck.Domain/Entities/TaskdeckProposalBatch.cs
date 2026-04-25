using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// A batch of <see cref="AutomationProposal"/>s generated from a single
/// <see cref="IntentEnvelopeV1"/>. Groups proposals for atomic review and
/// provides traceability back to the originating envelope.
/// </summary>
public class TaskdeckProposalBatch : Entity
{
    public Guid EnvelopeId { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public ProposalBatchStatus Status { get; private set; }

    /// <summary>
    /// Human-readable summary of the batch, typically derived from the
    /// envelope's top-ranked intent candidates.
    /// </summary>
    public string Summary { get; private set; } = string.Empty;

    /// <summary>
    /// The schema version used to generate this batch. Allows downstream
    /// consumers to handle format evolution gracefully.
    /// </summary>
    public int SchemaVersion { get; private set; }

    private readonly List<Guid> _proposalIds = new();

    /// <summary>
    /// IDs of the <see cref="AutomationProposal"/>s contained in this batch.
    /// Stored as a flat list rather than navigation properties to avoid tight
    /// coupling between the new intent pipeline and the existing proposal model.
    /// </summary>
    public IReadOnlyList<Guid> ProposalIds => _proposalIds.AsReadOnly();

    // Navigation
    public IntentEnvelopeV1 Envelope { get; private set; } = null!;

    private TaskdeckProposalBatch() { } // EF Core

    public TaskdeckProposalBatch(
        Guid envelopeId,
        Guid requestedByUserId,
        string summary,
        int schemaVersion = 1)
    {
        if (envelopeId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "EnvelopeId cannot be empty");
        if (requestedByUserId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "RequestedByUserId cannot be empty");
        if (string.IsNullOrWhiteSpace(summary))
            throw new DomainException(ErrorCodes.ValidationError, "Summary cannot be empty");
        if (summary.Length > 1000)
            throw new DomainException(ErrorCodes.ValidationError, "Summary cannot exceed 1000 characters");
        if (schemaVersion < 1)
            throw new DomainException(ErrorCodes.ValidationError, "SchemaVersion must be at least 1");

        EnvelopeId = envelopeId;
        RequestedByUserId = requestedByUserId;
        Summary = summary;
        SchemaVersion = schemaVersion;
        Status = ProposalBatchStatus.Draft;
    }

    public void AddProposalId(Guid proposalId)
    {
        if (proposalId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "ProposalId cannot be empty");
        if (_proposalIds.Contains(proposalId))
            throw new DomainException(ErrorCodes.Conflict, "ProposalId is already in this batch");
        if (Status != ProposalBatchStatus.Draft)
            throw new DomainException(ErrorCodes.InvalidOperation,
                "Cannot add proposals after batch has been sealed");

        _proposalIds.Add(proposalId);
        Touch();
    }

    public void Seal()
    {
        if (Status != ProposalBatchStatus.Draft)
            throw new DomainException(ErrorCodes.InvalidOperation,
                $"Cannot seal batch in status {Status}");
        if (_proposalIds.Count == 0)
            throw new DomainException(ErrorCodes.ValidationError,
                "Cannot seal an empty batch");

        Status = ProposalBatchStatus.Sealed;
        Touch();
    }

    public void Complete()
    {
        if (Status != ProposalBatchStatus.Sealed)
            throw new DomainException(ErrorCodes.InvalidOperation,
                $"Cannot complete batch in status {Status}");

        Status = ProposalBatchStatus.Completed;
        Touch();
    }

    public void Discard()
    {
        if (Status == ProposalBatchStatus.Completed)
            throw new DomainException(ErrorCodes.InvalidOperation,
                "Cannot discard a completed batch");

        Status = ProposalBatchStatus.Discarded;
        Touch();
    }
}

public enum ProposalBatchStatus
{
    Draft,
    Sealed,
    Completed,
    Discarded
}
