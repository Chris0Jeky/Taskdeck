namespace Taskdeck.Application.DTOs;

/// <summary>
/// What one successful proposal execution actually did.
/// </summary>
/// <param name="AlreadyApplied">
/// True when the proposal was already Applied when the call arrived, so this call wrote nothing.
/// This is the idempotent-replay branch single execute has always had; the batch surface reports it
/// as <see cref="BatchExecuteOutcome.Skipped"/> rather than claiming a second apply.
/// </param>
/// <param name="AppliedOperationCount">
/// Operations executed by THIS call. Zero on the already-applied branch, because this call executed
/// none — it is deliberately not a re-derived count of the historical apply.
/// </param>
public readonly record struct ProposalExecutionReceipt(bool AlreadyApplied, int AppliedOperationCount);
