namespace Taskdeck.Domain.Enums;

/// <summary>
/// Defines the operational scope of an agent tool.
/// </summary>
public enum ToolScope
{
    /// <summary>Tool operates on a specific board (columns, cards, labels).</summary>
    Board = 0,

    /// <summary>Tool operates on the capture inbox (triage, categorization).</summary>
    Inbox = 1,

    /// <summary>Tool operates at workspace/global level (settings, cross-board).</summary>
    Global = 2
}
