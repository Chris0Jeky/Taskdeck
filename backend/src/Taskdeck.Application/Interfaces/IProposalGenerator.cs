using Taskdeck.Domain.Common;
using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

/// <summary>
/// Generates typed proposal batches from intent envelopes with field-level
/// provenance and verification. This is the orchestration layer that ties
/// together deterministic pre-extraction, LLM-based inference, and
/// provenance verification into a complete proposal generation pipeline.
/// </summary>
public interface IProposalGenerator
{
    /// <summary>
    /// Generates a proposal batch from a fully-populated intent envelope.
    /// The envelope must be in Extracting status with at least one intent candidate.
    /// </summary>
    /// <param name="envelope">The intent envelope containing source blocks and intent candidates.</param>
    /// <param name="boardId">The board context for proposal generation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the generation output with proposals, provenance, and verification results.</returns>
    Task<Result<ProposalGenerationResult>> GenerateAsync(
        IntentEnvelopeV1 envelope,
        Guid boardId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The output of a proposal generation run. Contains the batch, per-field provenance,
/// and verification results for each generated proposal.
/// </summary>
public sealed class ProposalGenerationResult
{
    public TaskdeckProposalBatch Batch { get; }
    public IReadOnlyList<GeneratedProposal> Proposals { get; }
    public string ModelId { get; }
    public int TotalTokens { get; }

    public ProposalGenerationResult(
        TaskdeckProposalBatch batch,
        IReadOnlyList<GeneratedProposal> proposals,
        string modelId,
        int totalTokens)
    {
        Batch = batch ?? throw new ArgumentNullException(nameof(batch));
        Proposals = proposals ?? throw new ArgumentNullException(nameof(proposals));
        ModelId = modelId ?? throw new ArgumentNullException(nameof(modelId));
        TotalTokens = totalTokens;
    }
}

/// <summary>
/// A single generated proposal with its provenance and field verification results.
/// </summary>
public sealed class GeneratedProposal
{
    public Guid ProposalId { get; }
    public string Summary { get; }
    public string ActionType { get; }
    public ProposalProvenance Provenance { get; }
    public IReadOnlyList<FieldVerificationResult> VerificationResults { get; }

    public GeneratedProposal(
        Guid proposalId,
        string summary,
        string actionType,
        ProposalProvenance provenance,
        IReadOnlyList<FieldVerificationResult> verificationResults)
    {
        ProposalId = proposalId;
        Summary = summary ?? throw new ArgumentNullException(nameof(summary));
        ActionType = actionType ?? throw new ArgumentNullException(nameof(actionType));
        Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
        VerificationResults = verificationResults ?? throw new ArgumentNullException(nameof(verificationResults));
    }
}
