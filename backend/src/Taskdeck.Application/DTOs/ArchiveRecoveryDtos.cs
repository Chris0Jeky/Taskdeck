using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.DTOs;

public record CreateArchiveItemDto(
    string EntityType,
    Guid EntityId,
    Guid BoardId,
    string Name,
    Guid ArchivedByUserId,
    string SnapshotJson,
    string? Reason
);

public record ArchiveItemDto(
    Guid Id,
    string EntityType,
    Guid EntityId,
    Guid BoardId,
    string Name,
    Guid ArchivedByUserId,
    DateTime ArchivedAt,
    string? Reason,
    RestoreStatus RestoreStatus,
    DateTime? RestoredAt,
    Guid? RestoredByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public record RestoreArchiveItemDto(
    Guid? TargetBoardId,
    RestoreMode RestoreMode,
    ConflictStrategy ConflictStrategy
);

public record RestoreResult(
    bool Success,
    Guid? RestoredEntityId,
    string? ErrorMessage,
    string? ResolvedName
);

public enum RestoreMode
{
    InPlace,
    Copy
}

public enum ConflictStrategy
{
    Fail,
    Rename,
    AppendSuffix
}
