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
        if (Status == RequestStatus.Completed || Status == RequestStatus.Cancelled)
            throw new DomainException(
                ErrorCodes.ValidationError,
                $"Cannot process request in {Status} status");

        Status = RequestStatus.Processing;
        Touch();
    }

    public void MarkAsCompleted()
    {
        Status = RequestStatus.Completed;
        ProcessedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void MarkAsFailed(string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
            throw new DomainException(ErrorCodes.ValidationError, "Error message cannot be empty");

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
}
