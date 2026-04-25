using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// Version 1 of the intent envelope -- the versioned spine for the review-first
/// AI pipeline. Wraps raw user input (<see cref="SourceBlock"/>s), extracted
/// intents (<see cref="IntentCandidate"/>s), and the resulting
/// <see cref="TaskdeckProposalBatch"/>.
///
/// Lifecycle: Created → Populated (blocks + intents) → Processed (batch sealed).
/// </summary>
public class IntentEnvelopeV1 : Entity
{
    /// <summary>
    /// Schema version. Always 1 for this class. Future envelopes will
    /// increment this, enabling consumers to handle format evolution.
    /// </summary>
    public int Version { get; private set; } = 1;

    /// <summary>
    /// Where the envelope originated: "capture", "chat", "import", etc.
    /// </summary>
    public string Source { get; private set; } = string.Empty;

    /// <summary>
    /// Timestamp when the raw input was captured (may differ from
    /// <see cref="Entity.CreatedAt"/> if the envelope is created after
    /// a processing delay).
    /// </summary>
    public DateTimeOffset CapturedAt { get; private set; }

    /// <summary>
    /// The raw, unprocessed content that was captured. Retained for
    /// auditability and replay.
    /// </summary>
    public string RawContent { get; private set; } = string.Empty;

    /// <summary>
    /// Current processing status of the envelope.
    /// </summary>
    public EnvelopeStatus Status { get; private set; }

    /// <summary>
    /// The user who triggered the capture.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Optional correlation ID linking to the originating capture item,
    /// chat session, or other upstream entity.
    /// </summary>
    public string? CorrelationId { get; private set; }

    /// <summary>
    /// Reason for failure when status is <see cref="EnvelopeStatus.Failed"/>.
    /// Null when the envelope has not failed.
    /// </summary>
    public string? FailureReason { get; private set; }

    private readonly List<SourceBlock> _sourceBlocks = new();
    public IReadOnlyList<SourceBlock> SourceBlocks => _sourceBlocks.AsReadOnly();

    private readonly List<IntentCandidate> _intentCandidates = new();
    public IReadOnlyList<IntentCandidate> IntentCandidates => _intentCandidates.AsReadOnly();

    private readonly List<TaskdeckProposalBatch> _batches = new();
    public IReadOnlyList<TaskdeckProposalBatch> Batches => _batches.AsReadOnly();

    private IntentEnvelopeV1() { } // EF Core

    public IntentEnvelopeV1(
        string source,
        string rawContent,
        Guid userId,
        DateTimeOffset? capturedAt = null,
        string? correlationId = null)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new DomainException(ErrorCodes.ValidationError, "Source cannot be empty");
        if (source.Length > 50)
            throw new DomainException(ErrorCodes.ValidationError, "Source cannot exceed 50 characters");
        if (string.IsNullOrWhiteSpace(rawContent))
            throw new DomainException(ErrorCodes.ValidationError, "RawContent cannot be empty");
        if (rawContent.Length > 100_000)
            throw new DomainException(ErrorCodes.ValidationError, "RawContent cannot exceed 100000 characters");
        if (userId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "UserId cannot be empty");

        Source = source;
        RawContent = rawContent;
        UserId = userId;
        CapturedAt = capturedAt ?? DateTimeOffset.UtcNow;
        CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? null : correlationId.Trim();
        Status = EnvelopeStatus.Created;
    }

    public SourceBlock AddSourceBlock(
        int position,
        string content,
        string sourceType,
        string? sourceReferenceId = null)
    {
        if (Status != EnvelopeStatus.Created)
            throw new DomainException(ErrorCodes.InvalidOperation,
                "Cannot add source blocks after envelope processing has started");

        var block = new SourceBlock(Id, position, content, sourceType, sourceReferenceId);
        _sourceBlocks.Add(block);
        Touch();
        return block;
    }

    public IntentCandidate AddIntentCandidate(
        string label,
        double confidence,
        int rank,
        string? actionType = null)
    {
        if (Status == EnvelopeStatus.Processed || Status == EnvelopeStatus.Failed)
            throw new DomainException(ErrorCodes.InvalidOperation,
                $"Cannot add intent candidates when envelope is in status {Status}");

        if (Status == EnvelopeStatus.Created)
            Status = EnvelopeStatus.Extracting;

        var candidate = new IntentCandidate(Id, label, confidence, rank, actionType);
        _intentCandidates.Add(candidate);
        Touch();
        return candidate;
    }

    public TaskdeckProposalBatch CreateBatch(
        Guid requestedByUserId,
        string summary,
        int schemaVersion = 1)
    {
        if (Status != EnvelopeStatus.Extracting)
            throw new DomainException(ErrorCodes.InvalidOperation,
                $"Cannot create batches when envelope is in status {Status}");

        var batch = new TaskdeckProposalBatch(Id, requestedByUserId, summary, schemaVersion);
        _batches.Add(batch);
        Touch();
        return batch;
    }

    public void MarkProcessed()
    {
        if (Status != EnvelopeStatus.Extracting)
            throw new DomainException(ErrorCodes.InvalidOperation,
                $"Cannot mark as processed when envelope is in status {Status}");
        if (_intentCandidates.Count == 0)
            throw new DomainException(ErrorCodes.ValidationError,
                "Cannot mark as processed without at least one intent candidate");

        Status = EnvelopeStatus.Processed;
        Touch();
    }

    public void MarkFailed(string? reason = null)
    {
        if (Status == EnvelopeStatus.Processed)
            throw new DomainException(ErrorCodes.InvalidOperation,
                "Cannot mark an already-processed envelope as failed");

        Status = EnvelopeStatus.Failed;
        FailureReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Touch();
    }
}

public enum EnvelopeStatus
{
    Created,
    Extracting,
    Processed,
    Failed
}
