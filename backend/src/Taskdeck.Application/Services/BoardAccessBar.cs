namespace Taskdeck.Application.Services;

/// <summary>
/// Which membership bar <see cref="IAutomationPolicyEngine.ValidateBoardAccessAsync"/> (and the
/// gate <see cref="IAutomationPolicyEngine.ValidatePermissionsAsync"/> composes from it) applies
/// to the board a proposal targets.
///
/// <para>
/// The same requester/board gate serves two different kinds of caller, and #1836 proved they must
/// not share one bar: the MUTATION lanes (proposal creation in the worker/planner/chat lanes,
/// approve, and execute) need write-capable membership, while the READ lanes (the pending-proposal
/// diff and the terminal stored-preview read behind MCP <c>proposal_detail</c>, #1415) must stay
/// at plain membership — otherwise a board member demoted to Viewer loses the ability to read the
/// detail of proposals they authored themselves.
/// </para>
///
/// <para>
/// The parameter is deliberately required at every call site (no default): the bar is a property
/// of the LANE, not of the gate, so each caller states which one it is on. It is always chosen by
/// server-side code from the call site's own lane — never from request input.
/// </para>
/// </summary>
public enum BoardAccessBar
{
    /// <summary>
    /// Write-capable membership: <c>UserRole.Editor</c> as the minimum role, which is the exact
    /// set <c>BoardAccess.CanWrite()</c> admits (Owner, Admin, Editor) plus the board owner, whom
    /// <c>IBoardAccessRepository.HasAccessAsync</c> short-circuits separately. Mirrors the API-side
    /// #1794/#1827 <c>AuthorizationService.CanWriteBoardAsync</c> bar (#1836).
    ///
    /// <para>
    /// Deliberately the zero value so that a <c>default(BoardAccessBar)</c> — reached only by a
    /// caller writing <c>default</c> explicitly, since the parameter has no default — fails CLOSED
    /// on the stricter bar rather than silently opening a mutation lane to read-only members.
    /// </para>
    /// </summary>
    Write = 0,

    /// <summary>
    /// Any membership (Viewer included) plus the board owner — a <c>null</c> minimum role on
    /// <c>HasAccessAsync</c>. This is the bar every caller of this gate used before #1836 and the
    /// one the READ lanes keep: reading a proposal's own diff/stored preview is not a mutation, so
    /// it is gated on membership exactly as board content itself is.
    /// </summary>
    Read = 1
}
