namespace Taskdeck.Domain.Enums;

/// <summary>
/// Lifecycle of a persisted semantic candidate (ADR-0065 §Decision 5). A rerun supersedes rather
/// than duplicates; a user edit records a correction; nothing is rewritten in place.
/// </summary>
public enum SemanticCandidateState
{
    Proposed = 0,
    Corrected = 1,
    Accepted = 2,
    Dismissed = 3,
    Superseded = 4
}
