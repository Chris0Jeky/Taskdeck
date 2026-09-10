using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>An original recording is retained before any written answer exists. Confirmation is explicit.</summary>
public sealed class ThinkingAudioAnswer : Entity
{
    public Guid UserId { get; private set; }
    public Guid? BoardId { get; private set; }
    public Guid CardId { get; private set; }
    public Guid LayerId { get; private set; }
    public string QuestionHash { get; private set; } = string.Empty;
    public Guid CaptureId { get; private set; }
    public Guid SourceAssetId { get; private set; }
    public Guid UploadId { get; private set; }
    public long Revision { get; private set; } = 1;
    public Guid? RepresentationId { get; private set; }
    public Guid? ConfirmedMemoryId { get; private set; }
    public string? ConfirmationRequestHash { get; private set; }

    private ThinkingAudioAnswer() { }
    public ThinkingAudioAnswer(Guid userId, Guid boardId, Guid cardId, Guid layerId, string questionHash,
        Guid captureId, Guid sourceAssetId, Guid uploadId)
    {
        if (new[] { userId, boardId, cardId, layerId, captureId, sourceAssetId, uploadId }.Any(x => x == Guid.Empty)
            || questionHash.Length != 64 || !questionHash.All(Uri.IsHexDigit))
            throw new DomainException(ErrorCodes.ValidationError, "A saved question and original recording are required.");
        UserId = userId; BoardId = boardId; CardId = cardId; LayerId = layerId; QuestionHash = questionHash;
        CaptureId = captureId; SourceAssetId = sourceAssetId; UploadId = uploadId;
    }
    public void RecordWrittenVersion(Guid representationId)
    {
        if (representationId == Guid.Empty || ConfirmedMemoryId.HasValue)
            throw new DomainException(ErrorCodes.Conflict, "Correct confirmed answers in private memory.");
        RepresentationId = representationId; Revision++; Touch();
    }
    public void Confirm(Guid representationId, Guid memoryId, string requestHash)
    {
        if (representationId == Guid.Empty || memoryId == Guid.Empty || !RepresentationId.HasValue || ConfirmedMemoryId.HasValue)
            throw new DomainException(ErrorCodes.Conflict, "Save a written version before confirming the answer.");
        if (requestHash is null || requestHash.Length != 64 || !requestHash.All(Uri.IsHexDigit))
            throw new DomainException(ErrorCodes.ValidationError, "A confirmation receipt is required.");
        ConfirmationRequestHash = requestHash;
        RepresentationId = representationId; ConfirmedMemoryId = memoryId; Revision++; Touch();
    }
}
