using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>A person's explicit board-scoped knowledge, not an inferred board instruction.</summary>
public class WorkspaceMemory : Entity
{
    public Guid UserId { get; private set; }
    public Guid BoardId { get; private set; }
    public Guid? InsightId { get; private set; }
    public string? OriginalEvidence { get; private set; }
    public Guid? SourceCardId { get; private set; }
    public Guid? SourceLayerId { get; private set; }
    public long? SourceDeckRevision { get; private set; }
    public string? SourceQuestionHash { get; private set; }
    public Guid? SourceCaptureId { get; private set; }
    public Guid? AnswerSourceAssetId { get; private set; }
    public Guid? EvidenceSourceAssetId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Text { get; private set; } = string.Empty;
    public string OriginalText { get; private set; } = string.Empty;
    public string Status { get; private set; } = "statement";
    public bool Archived { get; private set; }
    public int Revision { get; private set; } = 1;
    public List<WorkspaceMemoryRevision> History { get; private set; } = new();

    private WorkspaceMemory() { }
    public WorkspaceMemory(Guid userId, Guid boardId, string title, string text, string status, Guid? insightId = null, string? originalEvidence = null)
    {
        Validate(title, text, status);
        UserId = userId; BoardId = boardId; Title = title; Text = text; OriginalText = text;
        Status = status; InsightId = insightId; OriginalEvidence = originalEvidence;
    }
    public void Revise(string title, string text, string status)
    {
        Validate(title, text, status);
        RememberRevision(); Title = title; Text = text; Status = status; Revision++; Touch();
    }
    public void AttachThinkingSource(Guid cardId, Guid layerId, long deckRevision, string questionHash)
    {
        if (SourceCardId.HasValue || cardId == Guid.Empty || layerId == Guid.Empty || deckRevision < 1 || questionHash.Length != 64)
            throw new DomainException(ErrorCodes.ValidationError, "Invalid thinking question source.");
        SourceCardId = cardId; SourceLayerId = layerId; SourceDeckRevision = deckRevision; SourceQuestionHash = questionHash;
    }
    public void SetArchived(bool archived)
    {
        if (Archived == archived) return;
        RememberRevision(); Archived = archived; Revision++; Touch();
    }
    public void RecordSources(Guid captureId, Guid answerAssetId, Guid? evidenceAssetId)
    {
        if (captureId == Guid.Empty || answerAssetId == Guid.Empty || evidenceAssetId == Guid.Empty
            || (SourceCaptureId.HasValue && SourceCaptureId != captureId))
            throw new DomainException(ErrorCodes.ValidationError, "Invalid private memory sources.");
        SourceCaptureId = captureId; AnswerSourceAssetId = answerAssetId; EvidenceSourceAssetId = evidenceAssetId;
    }
    private void RememberRevision() => History.Add(new WorkspaceMemoryRevision(Id, Title, Text, Status, Revision, Archived, AnswerSourceAssetId));
    public static void Validate(string title, string text, string status)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 240 || string.IsNullOrWhiteSpace(text) || text.Length > 8000)
            throw new DomainException(ErrorCodes.ValidationError, "Provide a title up to 240 characters and an answer up to 8,000 characters.");
        if (status is not ("statement" or "assumption" or "unknown" or "needsReview"))
            throw new DomainException(ErrorCodes.ValidationError, "Choose statement, assumption, unknown or needsReview.");
    }
}

public class WorkspaceMemoryRevision : Entity
{
    public Guid MemoryId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Text { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public int Revision { get; private set; }
    public bool Archived { get; private set; }
    public Guid? AnswerSourceAssetId { get; private set; }
    private WorkspaceMemoryRevision() { }
    public WorkspaceMemoryRevision(Guid memoryId, string title, string text, string status, int revision, bool archived, Guid? answerSourceAssetId = null)
    { MemoryId = memoryId; Title = title; Text = text; Status = status; Revision = revision; Archived = archived; AnswerSourceAssetId = answerSourceAssetId; }
    public void RecordAnswerSource(Guid assetId)
    {
        if (assetId == Guid.Empty || AnswerSourceAssetId.HasValue)
            throw new DomainException(ErrorCodes.ValidationError, "Invalid historical answer source.");
        AnswerSourceAssetId = assetId;
    }
}
