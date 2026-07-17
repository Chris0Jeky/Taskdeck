namespace Taskdeck.Domain.Enums;

/// <summary>
/// Lifecycle state of an <see cref="Taskdeck.Domain.Entities.LlmUsageRecord"/>.
/// A <see cref="Reserved"/> row is an in-flight quota reservation (issue #1313): it is
/// inserted atomically before the LLM network call to hold a request slot and an estimated
/// token amount, then either finalized to <see cref="Committed"/> with the actual token
/// counts or released (deleted) when the call produces no usage / fails. Only committed rows
/// count as real usage for reporting; both committed and still-live reserved rows count for
/// quota enforcement so concurrent callers cannot both pass at the boundary.
/// </summary>
public enum LlmUsageRecordStatus
{
    /// <summary>In-flight reservation holding a request slot and estimated tokens.</summary>
    Reserved = 0,

    /// <summary>Finalized usage with actual token counts.</summary>
    Committed = 1
}
