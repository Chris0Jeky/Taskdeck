using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

public class Notification : Entity
{
    public Guid UserId { get; private set; }
    public Guid? BoardId { get; private set; }
    public NotificationType Type { get; private set; }
    public NotificationCadence Cadence { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public string? SourceEntityType { get; private set; }
    public Guid? SourceEntityId { get; private set; }
    public string? DeduplicationKey { get; private set; }
    public bool IsRead { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }

    public User User { get; private set; } = null!;

    private Notification() : base()
    {
    }

    public Notification(
        Guid userId,
        NotificationType type,
        NotificationCadence cadence,
        string title,
        string message,
        Guid? boardId = null,
        string? sourceEntityType = null,
        Guid? sourceEntityId = null,
        string? deduplicationKey = null)
        : base()
    {
        if (userId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "User ID cannot be empty");

        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException(ErrorCodes.ValidationError, "Notification title cannot be empty");

        if (title.Length > 160)
            throw new DomainException(ErrorCodes.ValidationError, "Notification title cannot exceed 160 characters");

        if (string.IsNullOrWhiteSpace(message))
            throw new DomainException(ErrorCodes.ValidationError, "Notification message cannot be empty");

        if (message.Length > 2000)
            throw new DomainException(ErrorCodes.ValidationError, "Notification message cannot exceed 2000 characters");

        if (!string.IsNullOrWhiteSpace(sourceEntityType) && sourceEntityType.Length > 50)
            throw new DomainException(ErrorCodes.ValidationError, "Source entity type cannot exceed 50 characters");

        if (!string.IsNullOrWhiteSpace(deduplicationKey) && deduplicationKey.Length > 200)
            throw new DomainException(ErrorCodes.ValidationError, "Deduplication key cannot exceed 200 characters");

        UserId = userId;
        BoardId = boardId;
        Type = type;
        Cadence = cadence;
        Title = title;
        Message = message;
        SourceEntityType = sourceEntityType;
        SourceEntityId = sourceEntityId;
        DeduplicationKey = deduplicationKey;
        IsRead = false;
        ReadAt = null;
    }

    public void MarkAsRead()
    {
        if (IsRead)
            return;

        IsRead = true;
        ReadAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void MarkAsUnread()
    {
        if (!IsRead)
            return;

        IsRead = false;
        ReadAt = null;
        Touch();
    }
}

public enum NotificationType
{
    Mention,
    Assignment,
    ProposalOutcome,
    BoardChange,
    System
}

public enum NotificationCadence
{
    Immediate,
    Digest
}
