using Taskdeck.Domain.Entities;

namespace Taskdeck.Application.Interfaces;

/// <summary>
/// One expired-proposal sweep, already partitioned by ADR-0063's archived-board rule.
///
/// <para>
/// Expiry is a decision write: it moves a proposal out of <c>PendingReview</c> into the terminal
/// <c>Expired</c> status. ADR-0063 / #2168 make archived-board decision history read-only, and
/// <c>IAutomationPolicyEngine.GuardProposalDecisionWritesAsync</c> enforces that on the interactive
/// lanes. The automatic lanes cannot reuse that guard as-is — it *fails* the whole call, which is
/// wrong for a sweep that must keep expiring everything else — so the equivalent rule is applied
/// where the rows are selected instead. Returning the partition rather than a bare list is
/// deliberate: it is a compile-time break for any future caller, so a third expiry path cannot
/// silently reintroduce #2197 by ignoring the rule the way the worker and
/// <c>AutomationProposalService.ExpireProposalsAsync</c> both did.
/// </para>
/// </summary>
/// <param name="Expirable">
/// Expired <c>PendingReview</c> proposals that may be expired: board-less proposals, proposals whose
/// board row no longer exists, and proposals on an extant, non-archived board. This mirrors
/// <c>GetActiveByUserIdAsync</c>'s predicate exactly — only a positively identified extant archived
/// board is withheld, so dangling history is never silently dropped.
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
