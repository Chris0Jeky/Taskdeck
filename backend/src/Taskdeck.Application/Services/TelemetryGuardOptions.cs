namespace Taskdeck.Application.Services;

/// <summary>
/// Configures the TelemetryGuard allowlist and validation limits.
/// All metric keys must appear in the allowlist to be accepted.
/// </summary>
public sealed class TelemetryGuardOptions
{
    /// <summary>
    /// Maximum allowed string value length. Default: 256 characters.
    /// </summary>
    public int MaxStringLength { get; set; } = 256;

    /// <summary>
    /// Allowlisted metric keys. Only keys in this set pass validation.
    /// Populated with a sensible default set; extend via configuration.
    /// </summary>
    public HashSet<string> AllowedKeys { get; set; } = new(StringComparer.Ordinal)
    {
        // Capture flow metrics
        "capture.count",
        "capture.duration_ms",
        "capture.attachment_count",

        // Proposal / review metrics
        "proposal.generated_count",
        "proposal.accepted_count",
        "proposal.rejected_count",
        "proposal.edited_count",

        // Board metrics
        "board.card_count",
        "board.column_count",
        "board.active_count",

        // Session metrics
        "session.duration_ms",
        "session.action_count",

        // LLM usage metrics (content-free)
        "llm.request_count",
        "llm.token_input_count",
        "llm.token_output_count",
        "llm.latency_ms",
        "llm.error_count",

        // Automation metrics
        "automation.run_count",
        "automation.success_count",
        "automation.failure_count",

        // Workspace metrics
        "workspace.mode",
        "workspace.board_count",
    };

    public HashSet<string> StringValueKeys { get; set; } = new(StringComparer.Ordinal)
    {
        "workspace.mode",
    };

    public Dictionary<string, HashSet<string>> StringValueAllowlists { get; set; } =
        new(StringComparer.Ordinal)
        {
            ["workspace.mode"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "guided",
                "workbench",
                "agent",
            }
        };

    public HashSet<string> NumericValueKeys { get; set; } = new(StringComparer.Ordinal)
    {
        "capture.count",
        "capture.duration_ms",
        "capture.attachment_count",
        "proposal.generated_count",
        "proposal.accepted_count",
        "proposal.rejected_count",
        "proposal.edited_count",
        "board.card_count",
        "board.column_count",
        "board.active_count",
        "session.duration_ms",
        "session.action_count",
        "llm.request_count",
        "llm.token_input_count",
        "llm.token_output_count",
        "llm.latency_ms",
        "llm.error_count",
        "automation.run_count",
        "automation.success_count",
        "automation.failure_count",
        "workspace.board_count",
    };

    public HashSet<string> BooleanValueKeys { get; set; } = new(StringComparer.Ordinal);
}
