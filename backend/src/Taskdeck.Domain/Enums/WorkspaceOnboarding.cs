using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Enums;

public enum WorkspaceOnboardingVisibility
{
    Active = 0,
    Dismissed = 1
}

public enum WorkspaceOnboardingAction
{
    Dismiss = 0,
    Replay = 1
}

public static class WorkspaceOnboardingVisibilityContract
{
    public const string Active = "active";
    public const string Dismissed = "dismissed";

    public static bool TryParse(string? value, out WorkspaceOnboardingVisibility visibility)
    {
        visibility = WorkspaceOnboardingVisibility.Active;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        switch (value.Trim().ToLowerInvariant())
        {
            case Active:
                visibility = WorkspaceOnboardingVisibility.Active;
                return true;
            case Dismissed:
                visibility = WorkspaceOnboardingVisibility.Dismissed;
                return true;
            default:
                return false;
        }
    }

    public static string ToContractValue(this WorkspaceOnboardingVisibility visibility)
    {
        return visibility switch
        {
            WorkspaceOnboardingVisibility.Active => Active,
            WorkspaceOnboardingVisibility.Dismissed => Dismissed,
            _ => throw new DomainException(
                ErrorCodes.ValidationError,
                $"Unsupported workspace onboarding visibility '{visibility}'")
        };
    }
}

public static class WorkspaceOnboardingActionContract
{
    public const string Dismiss = "dismiss";
    public const string Replay = "replay";

    public static bool TryParse(string? value, out WorkspaceOnboardingAction action)
    {
        action = WorkspaceOnboardingAction.Dismiss;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        switch (value.Trim().ToLowerInvariant())
        {
            case Dismiss:
                action = WorkspaceOnboardingAction.Dismiss;
                return true;
            case Replay:
                action = WorkspaceOnboardingAction.Replay;
                return true;
            default:
                return false;
        }
    }
}
