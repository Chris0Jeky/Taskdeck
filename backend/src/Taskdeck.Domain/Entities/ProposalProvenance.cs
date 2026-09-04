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

    /// <summary>
    /// Identifier of the provider that produced the proposal (e.g., "openai", "deterministic").
    /// Null when nothing was recorded — the surface must then make no producer claim rather than
    /// guess one. Server-stamped at creation; never accepted from a client.
    /// </summary>
    public string? Provider { get; private set; }

    /// <summary>
    /// Version of the prompt contract that produced the proposal (e.g., "llm-triage.v2").
    /// Null when nothing was recorded. Server-stamped at creation; never accepted from a client.
    /// </summary>
    public string? PromptVersion { get; private set; }

    private readonly List<ProvenanceField> _fields = new();
    public IReadOnlyList<ProvenanceField> Fields => _fields.AsReadOnly();

    private ProposalProvenance() { } // EF Core

    public ProposalProvenance(
        Guid proposalId,
        string correlationId,
        string modelId,
        int totalTokens = 0,
        string? provider = null,
        string? promptVersion = null)
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
        if (provider is not null && provider.Length > MaxProviderLength)
            throw new DomainException(ErrorCodes.ValidationError, $"Provider cannot exceed {MaxProviderLength} characters");
        if (promptVersion is not null && promptVersion.Length > MaxPromptVersionLength)
            throw new DomainException(ErrorCodes.ValidationError, $"PromptVersion cannot exceed {MaxPromptVersionLength} characters");

        ProposalId = proposalId;
        CorrelationId = correlationId;
        ModelId = modelId;
        TotalTokens = totalTokens;
        // Blank is indistinguishable from "not recorded" on a trust surface, so it normalizes to
        // null rather than becoming an empty producer claim.
        Provider = NormalizeOptional(provider);
        PromptVersion = NormalizeOptional(promptVersion);
    }

    /// <summary>Maximum stored length of <see cref="Provider"/>.</summary>
    public const int MaxProviderLength = 64;

    /// <summary>Maximum stored length of <see cref="PromptVersion"/>.</summary>
    public const int MaxPromptVersionLength = 64;

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public void AddField(ProvenanceField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (field.ProposalProvenanceId != Id)
            throw new DomainException(ErrorCodes.ValidationError, "Field's ProposalProvenanceId must match this provenance's Id");

        _fields.Add(field);
        Touch();
    }
}
