namespace Taskdeck.Domain.Enums;

/// <summary>
/// Records what decision the user made on a proposal.
/// Used for outcome tracking and feedback loops -- must remain content-free (no PII).
/// </summary>
public enum OutcomeDecision
{
    /// <summary>
    /// User approved the proposal without modifications.
    /// </summary>
    Approved,

    /// <summary>
    /// User edited the proposal content before approving.
    /// </summary>
    EditedThenApproved,

    /// <summary>
    /// User explicitly rejected the proposal.
    /// </summary>
    Rejected,

    /// <summary>
    /// User saw the proposal but took no action (expired or dismissed).
    /// </summary>
    Ignored
}
