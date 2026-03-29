namespace Taskdeck.Domain.Enums;

/// <summary>
/// Risk classification for agent tools. Determines policy evaluation behavior.
/// High and Medium risk tools require review by default; Low risk tools
/// are still review-first unless explicitly configured otherwise.
/// </summary>
public enum ToolRiskLevel
{
    /// <summary>Read-only or informational tools with no mutation side effects.</summary>
    Low = 0,

    /// <summary>Tools that create or update entities within a bounded scope.</summary>
    Medium = 1,

    /// <summary>Tools that delete, archive, or perform cross-scope mutations.</summary>
    High = 2
}
