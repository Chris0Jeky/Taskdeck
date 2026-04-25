namespace Taskdeck.Domain.Entities;

/// <summary>
/// Represents the decision outcome for an automation proposal.
/// Content-free: captures what happened, not why.
/// </summary>
public enum OutcomeType
{
    /// <summary>Proposal approved without edits.</summary>
    Approved,

    /// <summary>Proposal edited then approved (revision chain exists).</summary>
    EditedThenApproved,

    /// <summary>Proposal explicitly rejected by user.</summary>
    Rejected,

    /// <summary>Proposal ignored (expired without decision).</summary>
    Ignored
}
