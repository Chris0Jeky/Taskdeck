using Taskdeck.Domain.Exceptions;

namespace Taskdeck.Domain.Enums;

public enum WorkspaceMode
{
    Guided = 0,
    Workbench = 1,
    Agent = 2
}

public static class WorkspaceModeContract
{
    public const string Guided = "guided";
    public const string Workbench = "workbench";
    public const string Agent = "agent";

    public static bool TryParse(string? value, out WorkspaceMode mode)
    {
        mode = WorkspaceMode.Guided;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        switch (value.Trim().ToLowerInvariant())
        {
            case Guided:
                mode = WorkspaceMode.Guided;
                return true;
            case Workbench:
                mode = WorkspaceMode.Workbench;
                return true;
            case Agent:
                mode = WorkspaceMode.Agent;
                return true;
            default:
                return false;
        }
    }

    public static string ToContractValue(this WorkspaceMode mode)
    {
        return mode switch
        {
            WorkspaceMode.Guided => Guided,
            WorkspaceMode.Workbench => Workbench,
            WorkspaceMode.Agent => Agent,
            _ => throw new DomainException(ErrorCodes.ValidationError, $"Unsupported workspace mode '{mode}'")
        };
    }
}
