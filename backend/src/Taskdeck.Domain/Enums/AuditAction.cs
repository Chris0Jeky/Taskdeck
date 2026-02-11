namespace Taskdeck.Domain.Enums;

/// <summary>
/// Defines the type of action performed on an entity for audit logging.
/// </summary>
public enum AuditAction
{
    Created = 0,
    Updated = 1,
    Deleted = 2,
    Archived = 3,
    Unarchived = 4,
    Moved = 5,
    PermissionGranted = 6,
    PermissionRevoked = 7,
    OwnershipTransferred = 8
}
