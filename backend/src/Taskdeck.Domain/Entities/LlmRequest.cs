using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// Represents a queued LLM request for processing when the LLM service is available.
/// Supports voicenotes, transcripts, and other request types.
/// </summary>
public class LlmRequest : Entity
{
    public Guid UserId { get; private set; }
    public Guid? BoardId { get; private set; }
    public Guid? TranscriptId { get; private set; }
    public string RequestType { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public RequestStatus Status { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public int RetryCount { get; private set; }

    // Navigation properties
    public User User { get; private set; } = null!;
    public Board? Board { get; private set; }

    private LlmRequest() : base() { }

    public LlmRequest(
        Guid userId,
        string requestType,
        string payload,
        Guid? boardId = null)
        : base()
    {
        if (userId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "User ID cannot be empty");

        if (string.IsNullOrWhiteSpace(requestType))
            throw new DomainException(ErrorCodes.ValidationError, "Request type cannot be empty");

        if (string.IsNullOrWhiteSpace(payload))
            throw new DomainException(ErrorCodes.ValidationError, "Payload cannot be empty");

        if (boardId.HasValue && boardId.Value == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Board ID cannot be empty");

        UserId = userId;
        RequestType = requestType;
        Payload = payload;
        BoardId = boardId;
        Status = RequestStatus.Pending;
        RetryCount = 0;
    }

    public void MarkAsProcessing()
    {
        if (Status == RequestStatus.Processing)
            throw new DomainException(
                ErrorCodes.ValidationError,
                "Request is already processing");

        if (Status == RequestStatus.Completed || Status == RequestStatus.Cancelled)
            throw new DomainException(
                ErrorCodes.ValidationError,
                $"Cannot process request in {Status} status");

        Status = RequestStatus.Processing;
        Touch();
    }

    public void MarkAsCompleted()
    {
        if (Status != RequestStatus.Processing)
            throw new DomainException(
                ErrorCodes.ValidationError,
                "Can only complete requests that are processing");

        Status = RequestStatus.Completed;
        ProcessedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void MarkAsFailed(string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
            throw new DomainException(ErrorCodes.ValidationError, "Error message cannot be empty");

        if (Status == RequestStatus.Completed || Status == RequestStatus.Cancelled)
            throw new DomainException(
                ErrorCodes.ValidationError,
                $"Cannot fail request in {Status} status");

        Status = RequestStatus.Failed;
        ErrorMessage = errorMessage;
        ProcessedAt = DateTimeOffset.UtcNow;
        RetryCount++;
        Touch();
    }

    public void Cancel()
    {
        if (Status == RequestStatus.Processing)
            throw new DomainException(
                ErrorCodes.ValidationError,
                "Cannot cancel request that is currently processing");

        if (Status == RequestStatus.Completed)
            throw new DomainException(
                ErrorCodes.ValidationError,
                "Cannot cancel request that is already completed");

        Status = RequestStatus.Cancelled;
        Touch();
    }

    public void ResetForRetry()
    {
        if (Status != RequestStatus.Failed)
            throw new DomainException(
                ErrorCodes.ValidationError,
                "Can only retry failed requests");

        Status = RequestStatus.Pending;
        ErrorMessage = null;
        ProcessedAt = null;
        Touch();
    }

    /// <summary>
    /// Returns a claimed (Processing) request to Pending WITHOUT charging the retry budget, for a
    /// claim abandoned by a graceful shutdown rather than by a failure (#1605). Deliberately distinct
    /// from <see cref="MarkAsFailed"/> + <see cref="ResetForRetry"/>, which increments
    /// <see cref="RetryCount"/> and is the failure/crash path: nothing about this attempt failed, so
    /// charging it would let enough restarts exhaust a healthy request's budget. Mirrors
    /// <c>OutboundWebhookDelivery.ReturnToPending</c>, which the webhook worker uses for the same
    /// shutdown case. ErrorMessage/ProcessedAt are left untouched: a Processing row was claimed from
    /// Pending, where ResetForRetry has already cleared both.
    /// </summary>
    public void ReleaseClaim()
    {
        if (Status != RequestStatus.Processing)
            throw new DomainException(
                ErrorCodes.ValidationError,
                "Can only release the claim on a processing request");

        Status = RequestStatus.Pending;
        Touch();
    }

    public void UpdatePayload(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            throw new DomainException(ErrorCodes.ValidationError, "Payload cannot be empty");

        Payload = payload;
        Touch();
    }

    public void BackfillBoard(Guid boardId)
    {
        if (boardId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Board ID cannot be empty");

        if (BoardId.HasValue)
        {
            if (BoardId.Value != boardId)
                throw new DomainException(ErrorCodes.ValidationError, "Cannot reassign request to a different board");

            return;
        }

        BoardId = boardId;
        Touch();
    }

    /// <summary>
    /// Links this queue request to its durable transcript. Replaying the same linkage is a
    /// no-op; assigning a different transcript would make the queue request ambiguous.
    /// </summary>
    public void AttachTranscript(Guid transcriptId)
    {
        if (transcriptId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Transcript ID cannot be empty");

        if (TranscriptId.HasValue)
        {
            if (TranscriptId.Value != transcriptId)
            {
                throw new DomainException(
                    ErrorCodes.Conflict,
                    "Cannot attach a different transcript to this request");
            }

            return;
        }

        TranscriptId = transcriptId;
        Touch();
    }
}
