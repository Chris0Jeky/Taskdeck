using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// Represents an audit log entry tracking changes to entities.
/// Provides history and accountability for all system actions.
/// </summary>
public class AuditLog : Entity
{
    public string EntityType { get; private set; } = string.Empty;
    public Guid EntityId { get; private set; }
    public AuditAction Action { get; private set; }
    public Guid? UserId { get; private set; }
    public string? Changes { get; private set; }
    public DateTimeOffset Timestamp { get; private set; }

    // Navigation property
    public User? User { get; private set; }

    private AuditLog() : base() { }

    public AuditLog(
        string entityType,
        Guid entityId,
        AuditAction action,
        Guid? userId = null,
        string? changes = null)
        : base()
    {
        if (string.IsNullOrWhiteSpace(entityType))
            throw new DomainException(ErrorCodes.ValidationError, "Entity type cannot be empty");

        if (entityId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Entity ID cannot be empty");

        if (userId.HasValue && userId.Value == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "User ID cannot be empty");

        EntityType = entityType;
        EntityId = entityId;
        Action = action;
        UserId = userId;
        Changes = changes;
        Timestamp = DateTimeOffset.UtcNow;
    }
}
