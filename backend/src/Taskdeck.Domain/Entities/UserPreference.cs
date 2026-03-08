using Taskdeck.Domain.Common;
using Taskdeck.Domain.Enums;
using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Entities;

public class UserPreference : Entity
{
    public Guid UserId { get; private set; }
    public WorkspaceMode WorkspaceMode { get; private set; }
    public WorkspaceOnboardingVisibility OnboardingVisibility { get; private set; }
    public DateTimeOffset? OnboardingDismissedAt { get; private set; }
    public DateTimeOffset? OnboardingCompletedAt { get; private set; }

    public User User { get; private set; } = null!;

    private UserPreference() : base()
    {
    }

    public UserPreference(
        Guid userId,
        WorkspaceMode workspaceMode = WorkspaceMode.Guided,
        WorkspaceOnboardingVisibility onboardingVisibility = WorkspaceOnboardingVisibility.Active)
        : base()
    {
        if (userId == Guid.Empty)
            throw new DomainException(ErrorCodes.ValidationError, "User ID cannot be empty");

        if (!Enum.IsDefined(workspaceMode))
            throw new DomainException(ErrorCodes.ValidationError, "Workspace mode is invalid");

        if (!Enum.IsDefined(onboardingVisibility))
            throw new DomainException(ErrorCodes.ValidationError, "Workspace onboarding visibility is invalid");

        UserId = userId;
        WorkspaceMode = workspaceMode;
        OnboardingVisibility = onboardingVisibility;
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

    public void DismissOnboarding()
    {
        OnboardingVisibility = WorkspaceOnboardingVisibility.Dismissed;
        OnboardingDismissedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void ReplayOnboarding()
    {
        OnboardingVisibility = WorkspaceOnboardingVisibility.Active;
        OnboardingDismissedAt = null;
        Touch();
    }

    public void RecordOnboardingCompletion()
    {
        if (OnboardingCompletedAt is not null)
            return;

        OnboardingCompletedAt = DateTimeOffset.UtcNow;
        Touch();
    }
}
