using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// Links a proposal to its full provenance chain: the set of fields,
/// their derivation kinds, source references, and confidence levels.
/// </summary>
public class ProposalProvenance : Entity
{
    /// <summary>
    /// The proposal this provenance chain belongs to.
    /// </summary>
    public Guid ProposalId { get; private set; }

    /// <summary>
    /// Correlation ID tying this provenance to the originating pipeline run.
    /// </summary>
    public string CorrelationId { get; private set; } = string.Empty;

    /// <summary>
    /// Identifier of the LLM model used to generate the proposal (e.g., "gpt-4o", "mock").
    /// </summary>
    public string ModelId { get; private set; } = string.Empty;

    /// <summary>
    /// Total token count consumed for this proposal generation (prompt + completion).
    /// </summary>
    public int TotalTokens { get; private set; }

    private readonly List<ProvenanceField> _fields = new();
    public IReadOnlyList<ProvenanceField> Fields => _fields.AsReadOnly();

    private ProposalProvenance() { } // EF Core

    public ProposalProvenance(
        Guid proposalId,
        string correlationId,
        string modelId,
        int totalTokens = 0)
    {
        if (proposalId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "ProposalId cannot be empty");
        if (string.IsNullOrWhiteSpace(correlationId))
            throw new DomainException(ErrorCodes.ValidationError, "CorrelationId cannot be empty");
        if (correlationId.Length > 100)
            throw new DomainException(ErrorCodes.ValidationError, "CorrelationId cannot exceed 100 characters");
        if (string.IsNullOrWhiteSpace(modelId))
            throw new DomainException(ErrorCodes.ValidationError, "ModelId cannot be empty");
        if (modelId.Length > 100)
            throw new DomainException(ErrorCodes.ValidationError, "ModelId cannot exceed 100 characters");
        if (totalTokens < 0)
            throw new DomainException(ErrorCodes.ValidationError, "TotalTokens cannot be negative");

        ProposalId = proposalId;
        CorrelationId = correlationId;
        ModelId = modelId;
        TotalTokens = totalTokens;
    }

    public void AddField(ProvenanceField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (field.ProposalProvenanceId != Id)
            throw new DomainException(ErrorCodes.ValidationError, "Field's ProposalProvenanceId must match this provenance's Id");

        _fields.Add(field);
        Touch();
    }
}
