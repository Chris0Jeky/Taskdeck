namespace Taskdeck.Domain.Enums;

/// <summary>
/// Status classification for a card history row in the proposal review ledger.
/// </summary>
public enum CardHistoryStatus
{
    /// <summary>
    /// This row represents an operation from the current proposal being reviewed (not yet applied).
    /// </summary>
    Pending = 0,

    /// <summary>
    /// This row represents a previously applied proposal operation.
    /// </summary>
    Applied = 1,

    /// <summary>
    /// This row represents other historical activity (audit log entries, non-proposal changes).
    /// </summary>
    Past = 2
}
