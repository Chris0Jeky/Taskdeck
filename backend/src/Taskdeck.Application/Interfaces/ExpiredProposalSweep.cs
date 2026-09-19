using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

/// <summary>
/// One expired-proposal sweep, partitioned by ADR-0063's archived-board rule.
///
/// <para>
/// Expiry is a decision write: it moves a proposal out of <c>PendingReview</c> into the terminal
/// <c>Expired</c> status. ADR-0063 / #2168 make archived-board decision history read-only. The
/// repository partition is the first guard: it withholds boards already known to be archived while
/// allowing unrelated active-board, boardless and dangling-board history to continue. It is not an
/// atomic authorization snapshot. Every automatic caller must pass <see cref="Expirable"/> board
/// references through <c>IAutomationPolicyEngine.GuardProposalDecisionWritesAsync</c> immediately
/// before mutation. That second guard rejects a board archived after selection and arms active board
/// concurrency markers so an archive committed later conflicts with the same expiry save (#2170).
/// </para>
///
/// <para>
/// Returning the partition rather than a bare list remains deliberate: it is a compile-time break
/// for any future caller, so a third expiry path cannot silently ignore the already-archived-board
/// rule. The second-stage guard is executable at the service/worker boundary because it must share
/// the exact scoped unit of work that persists the proposal transition.
/// </para>
/// </summary>
/// <param name="Expirable">
/// Expired <c>PendingReview</c> candidates whose board was not archived when the repository query
/// ran: board-less proposals, proposals whose board row no longer exists, and proposals on an
/// extant, non-archived board. This mirrors <c>GetActiveByUserIdAsync</c>'s predicate exactly — only
/// a positively identified extant archived board is withheld, so dangling history is never silently
/// dropped. Callers must still run the second-stage decision-write guard before mutation.
/// </param>
/// <param name="SkippedArchivedBoardCount">
/// How many otherwise-expirable proposals were withheld because their board is archived. Reported
/// so the sweep can say what it declined to touch instead of the rows vanishing silently. It is a
/// count only — never an id, summary, or board name — so it is safe to log. These proposals are not
/// lost: restoring the board makes them eligible again on the next sweep, which is the same
/// "restore before you can change it" contract the interactive lanes give.
/// </param>
public sealed record ExpiredProposalSweep(
    IReadOnlyList<AutomationProposal> Expirable,
    int SkippedArchivedBoardCount)
{
    public static readonly ExpiredProposalSweep Empty = new(Array.Empty<AutomationProposal>(), 0);
}
