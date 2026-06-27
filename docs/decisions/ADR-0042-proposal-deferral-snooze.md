# ADR-0042: Proposal Deferral (Snooze) via DeferredUntil with Expiry Protection

**Status:** Accepted

**Date:** 2026-06-27

**Deciders:** Repository maintainers

## Context

The Paper deep-Review surface (`PaperReviewView.vue`) ships a `D` keymap labelled "Defer 1h"
(spec: issue #1002). Until now the handler was a stub toast — there was no backend concept of
deferring/snoozing a proposal. A reviewer needs to push a `PendingReview` proposal out of the
active queue temporarily ("I'll deal with this later") and have it resurface after the window,
without any destructive change and without the proposal silently expiring while it is snoozed.

`AutomationProposal` already has an `ExpiresAt` (default 24h) and a background
`ProposalHousekeepingWorker` that transitions any `PendingReview` proposal past `ExpiresAt` to
`Expired`. There was no notion of "temporarily hidden but still pending".

## Decision

Model deferral as a **timing control, not a new status**. Add a nullable `DeferredUntil` to
`AutomationProposal` and a `Defer(TimeSpan duration)` domain method; the proposal stays
`PendingReview` and undecided.

1. **`DeferredUntil` field, not a `Deferred` status.** A new status would ripple through every
   `== PendingReview` guard (Approve/Reject/Expire/AddOperation), the housekeeping worker,
   `GetExpiredAsync`, and all frontend actionable checks, and would need a reverse transition.
   Deferral is orthogonal to the decision lifecycle, so a timing field is the smaller, safer change.

2. **`Defer` pushes `ExpiresAt` beyond `DeferredUntil` (+ a 24h grace).** A `DeferredUntil`-only
   model would let the housekeeping worker silently expire a near-expiry proposal mid-snooze and
   resurface it already-dead — violating the no-silent-expiry / review-first invariant. By pushing
   `ExpiresAt` past the snooze window, the existing worker stays correct with **zero worker
   changes**, and a resurfaced proposal always has a non-zero actionable window.

3. **Default 60 minutes, with an optional override clamped to [1, 1440].** Matches the literal
   "Defer 1h" keymap (the frontend sends no body) while leaving room for a future "defer until
   tomorrow" without an API break. An out-of-range `DurationMinutes` is **intentionally coerced
   (clamped), not rejected with 400** — consistent with the codebase's clamp house-style for
   bounded read/write parameters (e.g. the notification-paging negative-clamp guard) and benign
   for a snooze window. The domain `Defer()` still validates `[1, 1440]` as defense-in-depth for
   any non-HTTP caller.

4. **Re-deferral is unbounded and idempotent in effect.** Each `Defer` recomputes `DeferredUntil`
   from "now" (never stacks), so retries and double-presses are safe. There is no cap on the number
   of deferrals: a reviewer may keep snoozing a proposal. This is a deliberate trade — for a
   single-user, local-first tool the reviewer is explicitly choosing to snooze, and the action is
   **non-silent** (a success toast confirms it and `DeferredUntil` is exposed on the DTO). The cost
   is that the #1124 24h auto-expiry no longer bounds the lifetime of a repeatedly-snoozed item; this
   is accepted because no destructive change ever occurs and the snooze is always user-initiated.

5. **`DeferredUntil` is cleared on every exit from `PendingReview`** (Approve/Reject/Expire/Dismiss/
   MarkAsApplied — and `MarkAsFailed` for invariant uniformity, even though it is reached only via
   Approved where the snooze is already null), and the queue read filter is status-gated
   (`Status != PendingReview || DeferredUntil == null || DeferredUntil <= now`), so a decided
   proposal can never be hidden by a stale snooze value.

6. **No notification and no `ProposalOutcome` on defer.** Defer is a self-initiated UI action, so a
   notification would be noise; outcomes are terminal-decision telemetry only, and defer is not a
   decision.

Conflict detection (`GetPendingByOperationTargetAsync`) intentionally still sees deferred proposals:
a snoozed pending change still claims its target card, so it must keep participating in conflict
detection even while hidden from the queue.

## Alternatives

- **A new `ProposalStatus.Deferred`.** Rejected: state-machine churn across every status guard plus a
  reverse transition, for a concept that is orthogonal to the decision.
- **Extend `ExpiresAt` only (no `DeferredUntil`).** Rejected: keeps the proposal in the active queue,
  so it does not satisfy "leaves the review queue".
- **`DeferredUntil` only, modify the worker to skip deferred rows.** Rejected: a near-expiry proposal
  would still expire mid-snooze unless the worker also protected it; pushing `ExpiresAt` solves both
  with no worker change.
- **Cap the number/duration of deferrals.** Considered; deferred (see decision 4). Can be added later
  as a product policy without a schema change.

## Consequences

- Snoozed `PendingReview` proposals leave every list read and the Today/Home pending-review badge,
  and resurface via the live frontend clock (in-session) or the backend filter (cross-session).
- A deferred proposal is still fetchable by id / deep-link (`GetByIdAsync` is not filtered).
- A repeatedly-deferred proposal can outlive the 24h auto-expiry window; this is intentional and
  visible, not silent.
- Because `Defer` inflates `ExpiresAt`, a resurfaced deferred proposal sorts to the top of the
  `OrderByDescending(ExpiresAt)` selection window and — only if a user's pending set ever exceeds
  the list limit (impossible in the current single-user deployment) — could displace a fresher
  pending proposal out of the returned page. Tracked in **#1247** (fix = raw-SQL `CreatedAt`-ordered
  bounded selection, deferred because `CreatedAt` is a `DateTimeOffset` SQLite cannot `ORDER BY` in
  LINQ; disproportionate for an unreachable LOW).
- Migration `AddDeferredUntilToAutomationProposal` is additive (one nullable column, no backfill).

## References

- `backend/src/Taskdeck.Domain/Entities/AutomationProposal.cs` (`Defer`, `DeferredUntil`, clear-on-transition)
- `backend/src/Taskdeck.Infrastructure/Repositories/AutomationProposalRepository.cs` (status-gated queue filter)
- `POST /api/automation/proposals/{id}/defer` (`AutomationProposalsController`)
- Frontend: `useReviewProposals.isProposalDeferred`, `useReviewActions.handleDeferProposal`, `PaperReviewView.onDefer`
- Issue #1002 (Paper Review surface, "D Defer 1h"); #1124 (expiry semantics)
- ADR-0040 (UTC DateTime convention); sibling ADR-0043 (proposal quality feedback)
