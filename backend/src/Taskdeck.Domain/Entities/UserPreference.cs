using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

public class UserPreference : Entity
{
    public Guid UserId { get; private set; }
    public WorkspaceMode WorkspaceMode { get; private set; }

    public User User { get; private set; } = null!;

    private UserPreference() : base()
    {
    }

    public UserPreference(Guid userId, WorkspaceMode workspaceMode = WorkspaceMode.Guided)
        : base()
    {
        if (userId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "User ID cannot be empty");

        if (!Enum.IsDefined(workspaceMode))
            throw new DomainException(ErrorCodes.ValidationError, "Workspace mode is invalid");

        UserId = userId;
        WorkspaceMode = workspaceMode;
    }

    public static UserPreference CreateDefault(Guid userId)
    {
        return new UserPreference(userId);
    }

    public void UpdateWorkspaceMode(WorkspaceMode workspaceMode)
    {
        if (!Enum.IsDefined(workspaceMode))
            throw new DomainException(ErrorCodes.ValidationError, "Workspace mode is invalid");

        WorkspaceMode = workspaceMode;
        Touch();
    }
}
