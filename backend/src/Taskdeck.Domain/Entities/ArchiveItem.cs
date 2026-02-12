using Taskdeck.Domain.Common;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

public class ArchiveItem : Entity
{
    public string EntityType { get; private set; }
    public string EntityId { get; private set; }
    public string BoardId { get; private set; }
    public string Name { get; private set; }
    public string ArchivedByUserId { get; private set; }
    public DateTime ArchivedAt { get; private set; }
    public string? Reason { get; private set; }
    public string SnapshotJson { get; private set; }
    public RestoreStatus RestoreStatus { get; private set; }
    public DateTime? RestoredAt { get; private set; }
    public string? RestoredByUserId { get; private set; }

    private ArchiveItem() { } // EF Core

    public ArchiveItem(
        string entityType,
        string entityId,
        string boardId,
        string name,
        string archivedByUserId,
        string snapshotJson,
        string? reason = null)
    {
        if (string.IsNullOrWhiteSpace(entityType))
            throw new DomainException(ErrorCodes.ValidationError, "EntityType cannot be empty");
        if (entityType != "board" && entityType != "column" && entityType != "card")
            throw new DomainException(ErrorCodes.ValidationError, "EntityType must be 'board', 'column', or 'card'");
        if (string.IsNullOrWhiteSpace(entityId))
            throw new DomainException(ErrorCodes.ValidationError, "EntityId cannot be empty");
        if (string.IsNullOrWhiteSpace(boardId))
            throw new DomainException(ErrorCodes.ValidationError, "BoardId cannot be empty");
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException(ErrorCodes.ValidationError, "Name cannot be empty");
        if (name.Length > 200)
            throw new DomainException(ErrorCodes.ValidationError, "Name cannot exceed 200 characters");
        if (string.IsNullOrWhiteSpace(archivedByUserId))
            throw new DomainException(ErrorCodes.ValidationError, "ArchivedByUserId cannot be empty");
        if (string.IsNullOrWhiteSpace(snapshotJson))
            throw new DomainException(ErrorCodes.ValidationError, "SnapshotJson cannot be empty");

        EntityType = entityType;
        EntityId = entityId;
        BoardId = boardId;
        Name = name;
        ArchivedByUserId = archivedByUserId;
        ArchivedAt = DateTime.UtcNow;
        Reason = reason;
        SnapshotJson = snapshotJson;
        RestoreStatus = RestoreStatus.Available;
    }

    public void MarkAsRestored(string restoredByUserId)
    {
        if (string.IsNullOrWhiteSpace(restoredByUserId))
            throw new DomainException(ErrorCodes.ValidationError, "RestoredByUserId cannot be empty");
        if (RestoreStatus != RestoreStatus.Available)
            throw new DomainException(ErrorCodes.InvalidOperation, $"Cannot restore archive item with status {RestoreStatus}");

        RestoreStatus = RestoreStatus.Restored;
        RestoredAt = DateTime.UtcNow;
        RestoredByUserId = restoredByUserId;
        Touch();
    }

    public void MarkAsExpired()
    {
        if (RestoreStatus != RestoreStatus.Available)
            throw new DomainException(ErrorCodes.InvalidOperation, $"Cannot expire archive item with status {RestoreStatus}");

        RestoreStatus = RestoreStatus.Expired;
        Touch();
    }

    public void MarkAsConflict()
    {
        if (RestoreStatus != RestoreStatus.Available)
            throw new DomainException(ErrorCodes.InvalidOperation, $"Cannot mark conflict for archive item with status {RestoreStatus}");

        RestoreStatus = RestoreStatus.Conflict;
        Touch();
    }

    public void ResetToAvailable()
    {
        if (RestoreStatus == RestoreStatus.Restored)
            throw new DomainException(ErrorCodes.InvalidOperation, "Cannot reset already restored archive item");

        RestoreStatus = RestoreStatus.Available;
        Touch();
    }
}

public enum RestoreStatus
{
    Available,
    Restored,
    Expired,
    Conflict
}
