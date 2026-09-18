using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>A durable, explicitly requested processing receipt; never a confirmed written answer.</summary>
public sealed class AudioTranscriptionAttempt : Entity
{
    public Guid UserId { get; private set; }
    public Guid AudioAnswerId { get; private set; }
    public Guid CaptureId { get; private set; }
    public Guid SourceAssetId { get; private set; }
    public Guid RequestId { get; private set; }
    public string RequestHash { get; private set; } = "";
    public string ConfigurationHash { get; private set; } = "";
    public string Provider { get; private set; } = "";
    public string Model { get; private set; } = "";
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset Deadline { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public ProcessingJobState State { get; private set; } = ProcessingJobState.Running;
    public string? FailureCode { get; private set; }
    public Guid? RepresentationId { get; private set; }
    public long Revision { get; private set; } = 1;

    private AudioTranscriptionAttempt() { }
    public AudioTranscriptionAttempt(Guid userId, Guid audioAnswerId, Guid captureId, Guid sourceAssetId,
        Guid requestId, string requestHash, string configurationHash, string provider, string model,
        DateTimeOffset startedAt, DateTimeOffset deadline)
    {
        if (new[] { userId, audioAnswerId, captureId, sourceAssetId, requestId }.Any(x => x == Guid.Empty)
            || !ValidHash(requestHash) || !ValidHash(configurationHash)
            || string.IsNullOrWhiteSpace(provider) || provider.Length > 100 || provider.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(model) || model.Length > 100 || model.Any(char.IsControl)
            || startedAt == default || deadline <= startedAt || deadline > startedAt.AddMinutes(2))
            throw new DomainException(ErrorCodes.ValidationError, "A bounded transcription request and original recording are required.");
        UserId = userId; AudioAnswerId = audioAnswerId; CaptureId = captureId; SourceAssetId = sourceAssetId;
        RequestId = requestId; RequestHash = requestHash.ToLowerInvariant(); ConfigurationHash = configurationHash.ToLowerInvariant();
        Provider = provider; Model = model; StartedAt = startedAt; Deadline = deadline;
    }

    public bool Expire(DateTimeOffset now)
    {
        if (State != ProcessingJobState.Running || now < Deadline) return false;
        State = ProcessingJobState.Expired; FailureCode = "interrupted"; FinishedAt = now; Revision++; Touch();
        return true;
    }

    public void Complete(Guid representationId, DateTimeOffset now)
    {
        if (State != ProcessingJobState.Running || representationId == Guid.Empty || now < StartedAt || now >= Deadline)
            throw new DomainException(ErrorCodes.Conflict, "This transcription attempt can no longer publish a result.");
        RepresentationId = representationId; State = ProcessingJobState.Completed; FinishedAt = now; Revision++; Touch();
    }

    public void Fail(string code, DateTimeOffset now)
    {
        if (code is not ("provider-unavailable" or "provider-timeout" or "provider-response" or "input-unavailable" or "access-changed")
            || now < StartedAt)
            throw new DomainException(ErrorCodes.ValidationError, "Choose a supported transcription failure.");
        if (Expire(now)) return;
        if (State != ProcessingJobState.Running)
            throw new DomainException(ErrorCodes.Conflict, "This transcription attempt already has a final receipt.");
        State = ProcessingJobState.Failed; FailureCode = code; FinishedAt = now; Revision++; Touch();
    }

    private static bool ValidHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
