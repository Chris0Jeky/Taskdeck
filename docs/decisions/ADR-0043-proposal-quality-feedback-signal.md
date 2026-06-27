# ADR-0043: Proposal Quality Feedback as a Separate Content-Free Signal

**Status:** Accepted

**Date:** 2026-06-27

**Deciders:** Repository maintainers

## Context

The Paper deep-Review surface exposes a "Report" action (provenance drawer) for flagging an
automation proposal as a bad / unhelpful suggestion — negative feedback for the learning loop. The
handler was a stub toast; there was no backend to record the signal.

A `ProposalOutcome` entity already exists for **content-free, no-PII** decision telemetry
(`OutcomeDecision`: Approved / EditedThenApproved / Rejected / Ignored), intended to feed cohort
metrics (the `AutomationMetricsController.GetCohortMetrics` stub). The question was whether to fold a
"bad suggestion" signal into that decision telemetry or model it separately.

## Decision

Record quality feedback as a **new, separate, content-free `ProposalFeedback` entity/table**, not as
an extension of `ProposalOutcome`.

1. **Separate table, not an `OutcomeDecision` value.** `ProposalOutcome` encodes the single
   *decision* a user made; its `ToDecision`/`ToOutcomeType` bidirectional map and the
   `EditedFieldCount`↔`EditedThenApproved` coupling are brittle, `GetByProposalIdAsync` returns one
   outcome, and acceptance/edit/rejection rates are derived from it. A user can report a suggestion as
   bad **and** still approve/reject it, so feedback is a second, orthogonal axis. Injecting it as a
   decision would corrupt the cohort denominator and force every consumer to special-case a
   non-decision.

2. **Reporting never changes `ProposalStatus`.** Flagging is orthogonal feedback, not a board or
   decision action (review-first, no silent/destructive mutation). The proposal stays exactly where it
   was; the reviewer still explicitly chooses approve/reject/defer. Feedback is allowed on a proposal
   in **any** status (including after a decision).

3. **Structured reason category, never free text.** `ProposalFeedbackReason` is a closed enum
   (Unspecified / Irrelevant / Incorrect / Duplicate / TooRisky / Other), default `Unspecified` for
   today's one-click report. The entity has **no string field at all**, so the no-PII invariant is
   impossible to violate by construction. A future category picker can supply a specific reason without
   an API change; the controller rejects unknown/numeric reason values with 400 (`Enum.IsDefined`,
   because `Enum.TryParse` otherwise binds any numeric string to an undefined value).

4. **Structural idempotency, not a header.** A UNIQUE `(ProposalId, ReportedByUserId)` index is the
   hard guarantee of one signal per user per proposal; a service pre-check makes the common repeat a
   clean 204 no-op, and the `UnitOfWork` unique-violation mapping turns a true race into a benign
   success. `ReportedByUserId` comes only from claims, never the body (closes IDOR). On a repeat where
   the stored reason is `Unspecified` and a specific reason arrives, the row's reason is refined
   in place (last-specific-wins) — still one row.

5. **Read access, not write access.** Feedback mutates nothing on the board or proposal, so gating it
   behind board-edit rights would wrongly silence read-only reviewers whose quality signal is exactly
   what the learning loop wants. `AuthorizeProposalAsync(..., requireWriteAccess: false)` still 404s an
   unknown id and 403s a caller with no access.

The narrow TOCTOU window (the proposal is hard-deleted between the NotFound pre-check and the insert,
yielding an FK violation) is **accepted** as-is, consistent with the existing `ProposalRevision`
insert race; cascade-delete of feedback relies on the Microsoft.Data.Sqlite foreign-keys-on default.

## Alternatives

- **Add `ReportedBadSuggestion` to `OutcomeDecision`/`OutcomeType` and reuse `ProposalOutcome`.**
  Rejected: corrupts the single-decision telemetry and its cohort denominator (decision 1).
- **A bare boolean "reported" flag on the proposal.** Rejected: loses the audit trail, the reason
  dimension, and the per-user idempotency key.
- **Free-text reason.** Rejected: violates the content-free / no-PII convention.
- **Idempotency-Key header (like `/execute`).** Rejected: pushes idempotency bookkeeping onto the
  client for no benefit on a single-shot telemetry event; the natural `(proposal, user)` key is better.

## Consequences

- A new additive `ProposalFeedbacks` table (migration `AddProposalFeedback`); the proposal read path
  and queue/awaiting/stale counts are untouched.
- The signal is **persisted only** for now — cohort metrics stay empty until the learning-loop data
  layer is built (#1142); a future `reported_bad_rate` metric reads `ProposalFeedbacks` by user/period
  (the `(ReportedByUserId, CreatedAt)` index covers this) without distorting the `ProposalOutcome`-derived rates.
- A standalone `Reason` index is intentionally **deferred** until a category picker ships (every v1 row
  is `Unspecified`, so the index has no selectivity yet).
- The no-PII guarantee depends on the entity never gaining a text field; a future "add a comment"
  request would reintroduce PII risk and must be revisited here.
- **Concurrency contract for reason refinement.** The precise guarantee is "last-specific-wins for
  *sequential* re-reports; first-committed-wins under *simultaneous distinct* reasons." If one user
  fires two requests upgrading the same `Unspecified` row to two different specific reasons at once,
  the first commit wins on the `UpdatedAt` concurrency token and the second is mapped Conflict→benign
  success. This is accepted (negligible for a single-user signal; row integrity is preserved) rather
  than retried, to avoid an optimistic-retry loop on a non-critical telemetry write.
- **Data portability.** A user's feedback rows are content-free user-scoped data, so they are
  included in the GDPR data export (both the in-memory `ExportUserDataAsync` and the streaming path)
  as `UserDataExportProposalFeedbackDto` (proposal id, reason, reported-at). The export reads through
  a dedicated **uncapped** `GetAllByUserIdForExportAsync`, deliberately distinct from the cohort
  read `GetAllByUserIdAsync`, which caps at a 1000-row sample (for the future reported-bad-rate
  metric). Reusing the capped cohort read would silently truncate a heavy reporter's portability
  export (#1245 Codex review). The export read sorts newest-first in memory (it materializes the
  whole set anyway), sidestepping SQLite's inability to `ORDER BY` a `DateTimeOffset` column in
  LINQ — the same landmine that forced the cohort read onto a raw-SQL `ORDER BY CreatedAt DESC`.
- **Account-deletion retention.** `ProposalFeedback` is **not** swept by `AccountDeletionService`,
  matching the existing treatment of `AutomationProposal` (also retained): the FK is to
  `AutomationProposals`, not `Users`, and the `User` row is anonymized on deletion so the retained
  `ReportedByUserId` GUID resolves only to scrubbed data. Feedback therefore follows the same
  pseudonymized-GUID retention model as proposals — a deliberate decision, not an oversight; if
  proposals are ever added to the deletion sweep, feedback must be added alongside them. This
  decision is locked by `AccountDeletionServiceTests.DeleteAccountAsync_DeliberatelyRetainsProposalFeedback`
  (asserts the service never touches `ProposalFeedbacks`), so a future change to delete it must
  consciously update both the test and this ADR.

## References

- `backend/src/Taskdeck.Domain/Entities/ProposalFeedback.cs`, `Enums/ProposalFeedbackReason.cs`
- `backend/src/Taskdeck.Application/Services/ProposalFeedbackService.cs` (idempotent, last-specific-wins)
- `POST /api/automation/proposals/{id}/feedback` (`AutomationProposalsController.ReportFeedback`)
- `ProposalOutcome.cs` (the content-free decision-telemetry convention this mirrors)
- Frontend: `automationApi.reportBadSuggestion`, `PaperReviewView.onReportBadSuggestion`
- #1142 (cohort-metrics tracker); sibling ADR-0042 (proposal deferral)
