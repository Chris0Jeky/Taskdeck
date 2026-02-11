using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

/// <summary>
/// Represents a user's access level to a specific board.
/// Defines who can view, edit, or manage a board.
/// </summary>
public class BoardAccess : Entity
{
    public Guid BoardId { get; private set; }
    public Guid UserId { get; private set; }
    public UserRole Role { get; private set; }
    public Guid GrantedBy { get; private set; }
    public DateTimeOffset GrantedAt { get; private set; }

    // Navigation properties
    public Board Board { get; private set; } = null!;
    public User User { get; private set; } = null!;

    private BoardAccess() : base() { }

    public BoardAccess(Guid boardId, Guid userId, UserRole role, Guid grantedBy)
        : base()
    {
        if (boardId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "Board ID cannot be empty");

        if (userId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "User ID cannot be empty");

        if (grantedBy == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "GrantedBy user ID cannot be empty");

        BoardId = boardId;
        UserId = userId;
        Role = role;
        GrantedBy = grantedBy;
        GrantedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateRole(UserRole newRole, Guid updatedBy)
    {
        if (updatedBy == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "UpdatedBy user ID cannot be empty");

        Role = newRole;
        GrantedBy = updatedBy;
        GrantedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public bool CanRead() => true; // All access levels can read

    public bool CanWrite() => Role == UserRole.Owner || Role == UserRole.Admin || Role == UserRole.Editor;

    public bool CanManageAccess() => Role == UserRole.Owner || Role == UserRole.Admin;

    public bool CanDelete() => Role == UserRole.Owner;
}
