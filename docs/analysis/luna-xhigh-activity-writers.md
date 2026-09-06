# Human-activity writer inventory

Status: source-backed analysis for #1480
Last verified: 2026-09-06
Base inspected: `origin/main` at `16963a79b1357e284fd4e403e70f833925b3105e`

## Purpose and boundary

This report inventories the writers behind the date columns used by
`scripts/dogfooding/dogfood-snapshot.py`. It separates human-initiated, automation/background,
mixed, and unattributed paths so a later metric decision does not mistake a storage timestamp
for proof of user activity.

This is an analysis-only slice. It does not change the active-day formula, add fixture filtering,
add account attribution, or choose database precedence. The correct output of this slice is the
writer evidence and the remaining decision boundary.

## Current active-day calculation

The script unions distinct calendar dates from these columns (`dogfood-snapshot.py:235-253`):

| Table / column | Current role | Writer classification |
| --- | --- | --- |
| `AuditLogs.Timestamp` | Any recorded audit event | Mixed: direct user CRUD, imports/recovery, and proposal execution can all write audit rows |
| `Cards.CreatedAt` | Card creation day | Mixed: direct CRUD, imports, restore, starter packs, and proposal application |
| `AutomationProposals.CreatedAt` | Proposal creation day | Mixed: API/MCP/chat requests, planner/triage, and background agents |
| `AutomationProposals.DecidedAt` | Review decision day | Strong user-decision signal in the current review-first flow, but the metric does not retain actor attribution |
| `AutomationProposals.AppliedAt` | Apply completion day | Strong user-driven workflow signal, but the metric does not retain actor attribution |
| `Cards.UpdatedAt` | Card edit/move day | Mixed: direct user edits and non-interactive import/restore/apply paths |
| `ChatMessages.CreatedAt` | Chat message day | User and assistant messages are both created by a user chat request; no independent background writer was found in the inspected path |
| `LlmRequests.CreatedAt` | Capture/request intake day | Mixed: capture/queue APIs, imports and integrations can create rows under a user or adapter identity |

`Boards.CreatedAt` and `Boards.UpdatedAt` are not in the union. Board-only activity is therefore
not represented unless another audit event happens to accompany it. `LlmRequests.UpdatedAt` and
`AutomationProposals.UpdatedAt` are also not in the union; that avoids machine noise but loses
some row-reusing human actions.

## Writer evidence

### Common timestamp and audit primitives

- `backend/src/Taskdeck.Domain/Common/Entity.cs:3-24` initializes `CreatedAt` and `UpdatedAt`
  for every entity and `Touch()` stamps `UpdatedAt` with the server clock. The timestamp itself
  carries no actor or source information.
- `backend/src/Taskdeck.Domain/Entities/AuditLog.cs:10-56` initializes `Timestamp` at audit-row
  construction and stores an optional `UserId`. An audit date is therefore only as attributable
  as the writer that supplied that optional user id.
- `backend/src/Taskdeck.Application/Services/HistoryService.cs:49-76` persists audit rows but
  does not classify the caller.
- `backend/src/Taskdeck.Application/Services/AuditLogWriter.cs:7-64` makes audit persistence
  non-fatal and warning-visible; it does not add actor provenance.

### Boards and cards

- `BoardService.CreateBoardInternalAsync` creates a `Board`, saves it, then writes a `Created`
  audit row with `ownerId` (`BoardService.cs:262-268`). `UpdateBoardInternalAsync` writes
  `Updated`, `Archived`, or `Unarchived` with the acting user when the overload has one
  (`BoardService.cs:297-319`). These are useful human signals, but imports and archive restore
  also construct or mutate boards:
  `BoardJsonExportImportService.cs:129-130` and `RestoreExecutor.cs:82-88,100-106`.
- `CardService` performs direct create/update/move/delete through an actor-carrying overload and
  writes matching audit rows (`CardService.cs:53-106,184-188,321-325,425-429`). The proposal
  apply path deliberately calls the overload without a human actor; its audit provenance comes
  from the proposal (`CardService.cs:40-50` and `Pipeline/ExecutionAuditRecorder.cs:24-45`).
- `Card` mutation methods (`Card.cs:43-112`) call `Touch()` for update, due-date, position,
  move, block, and unblock operations. The same methods are used by direct CRUD, external import
  (`ExternalImportService.cs:267-285`), restore (`RestoreExecutor.cs:206-244`), starter packs,
  and proposal execution. `Cards.UpdatedAt` is therefore mixed, not a human-only signal.
- `Board.RecordDependentMutation()` intentionally does not touch `Board.UpdatedAt`
  (`Board.cs:77-111`). Dependent card writes cannot be treated as board metadata activity merely
  from that marker.

### Automation proposals

- `AutomationProposal` construction stamps `CreatedAt` through `Entity` and is reached by a
  mixed set of callers: the API controller, MCP write tools, `AutomationPlannerService`,
  `CaptureTriageService`, `ChatService`, inbox triage/digest agents, and proposal operation
  executors. The caller inventory is visible in the `CreateProposalAsync`/`CreateTranscriptProposalAsync`
  references under `backend/src`.
- `Approve` and `Reject` set `DecidedAt` and `DecidedByUserId` and then `Touch()`
  (`AutomationProposal.cs:112-160`). `ApproveProposalAsync` and `RejectProposalAsync` are the
  review decision services (`AutomationProposalService.cs:445-470,1197-1224`). Under the current
  review-first policy these are the closest available decision-day signals, but the dogfooding
  query unions only dates and discards the actor identity.
- `MarkAsApplied` and `MarkAsFailed` set terminal outcome timestamps and `Touch()`
  (`AutomationProposal.cs:147-160,196-211`). The executor invokes these after applying a proposal
  (`AutomationExecutorService.cs:614-623`). `AppliedAt` is the reliable apply evidence used by
  the same script for the funnel count (`dogfood-snapshot.py:301-307`), but it is not itself an
  actor-attributed activity record.
- `Defer` changes `DeferredUntil`, may extend `ExpiresAt`, and calls `Touch()`
  (`AutomationProposal.cs:172-193`). `DeferProposalAsync` explicitly writes no
  `ProposalOutcome` or notification (`AutomationProposalService.cs:1226-1250`). Because
  `UpdatedAt` is excluded, a deferral-only day has no current activity timestamp.
- `Expire` calls `Touch()` (`AutomationProposal.cs:213-218`) and is invoked by the unattended
  `ProposalHousekeepingWorker` (`ProposalHousekeepingWorker.cs:118-148`). Including
  `AutomationProposals.UpdatedAt` would therefore manufacture activity during idle operation;
  excluding it protects the metric but also hides the human deferral path above.

### Captures and LLM requests

- `CaptureService.CreateAsync` constructs and persists an `LlmRequest` at capture intake
  (`CaptureService.cs:176-242`). `LlmQueueService.EnqueueAsync` is a second queue-entry seam
  (`LlmQueueService.cs:60-103`) and performs canonical capture intake when the payload is a
  capture. `NoteImportService` delegates imported notes to `CaptureService.CreateAsync`
  (`NoteImportService.cs:130,218`), so `LlmRequests.CreatedAt` is not limited to a person typing
  in the capture composer.
- `LlmRequest.UpdatePayload`, `Cancel`, and processing state transitions all call `Touch()`
  (`LlmRequest.cs:88-154,186-233`). Human capture edits/cancellation use these existing-row
  mutations (`CaptureService.cs:607,829-843,932-964,1130-1162`); worker processing and retry
  paths also update the same entity (`LlmQueueToProposalWorker.cs:217-229,319-345,559-561,708-731`).
  `LlmRequests.UpdatedAt` is consequently mixed and is not currently queried.
- A capture-only edit or cancellation can therefore be real user activity without creating a new
  `LlmRequests` row, which explains the issue's lower-bound gap. Adding `UpdatedAt` directly would
  reintroduce worker noise and would need a writer-aware event or audit contract first.

### Chat messages

- `ChatService` creates the user message before the provider call and creates the assistant
  message after the response (`ChatService.cs:198-216,622-639`). The streaming path creates its
  terminal assistant message in the same service (`ChatService.cs:833-844`).
- `ChatMessage` itself only touches `UpdatedAt` for later token/proposal/tool metadata changes;
  the active-day script uses `CreatedAt`, which records the user chat request and its assistant
  response together. No separate background writer was found for `ChatMessages` in the inspected
  application/API paths.

## Demo and fixture contamination

`noise_board_ids()` identifies board names with configured demo/test prefixes
(`dogfood-snapshot.py:206-221`). The active-day loop runs before and independently of that set;
the result is used for the board-count warning at `dogfood-snapshot.py:272-282`, not to filter
the dates from `AuditLogs`, `Cards`, `AutomationProposals`, `ChatMessages`, or `LlmRequests`.
The current output explicitly tells operators to measure against a separate
`TASKDECK_DOGFOOD_DB` when fixture boards dominate. This slice does not turn the warning into an
attribution rule.

## Scenario matrix

| Scenario | Current signal | Classification / gap |
| --- | --- | --- |
| Create or edit a board | Board timestamp; often an audit row | Board timestamps are omitted, so coverage depends on audit success and writer path |
| Create/edit/move a card directly | Card `CreatedAt`/`UpdatedAt`, audit row | Mixed but usually user-attributable when actor-carrying CRUD is used |
| Apply a review proposal | `AppliedAt`, proposal-execution audit rows | Counts the workflow milestone; actor identity is not retained by the metric union |
| Approve or reject a proposal | `DecidedAt` | Strong review signal under current policy; still date-only in the metric |
| Defer a proposal only | `UpdatedAt` only | Missed by design because the column is also touched by housekeeping |
| Capture new text/transcript | `LlmRequests.CreatedAt` | Counted, but adapter/import and direct capture are mixed |
| Edit or cancel an existing capture | `LlmRequests.UpdatedAt` only | Missed by the current formula; adding the column directly is unsafe |
| Send a chat message | `ChatMessages.CreatedAt` | Counted; user and assistant rows represent one user-triggered exchange |
| Run proposal housekeeping while idle | `AutomationProposals.UpdatedAt` would move | Correctly excluded to avoid false activity |
| Touch only demo/test boards | Several counted tables can contribute dates | `noise_board_ids` warns but does not filter; separate database remains the current mitigation |

## Safe decision boundary

The evidence supports these conclusions without selecting a new product formula:

1. `UpdatedAt` is not a human-activity contract for either proposals or queue rows because both
   human and background writers use the same `Touch()` seam.
2. `DecidedAt`, `AppliedAt`, and actor-bearing audit rows are the strongest current signals for
   review activity, but the metric currently discards their actor/source dimensions.
3. Board-only activity and row-reusing capture activity are separate coverage gaps; one cannot be
   repaired honestly by adding every available `UpdatedAt` column.
4. Fixture exclusion needs either a separate database or an explicitly lower-bound metric whose
   excluded sources are disclosed. `noise_board_ids()` alone is not an attribution proof.

Any follow-up implementation should choose one explicit contract—event/audit attribution,
source-specific lower bounds, or a separately scoped measurement mode—before changing the active
day calculation. It should then add synthetic fixtures for direct, worker, import, and review-only
scenarios and prove that idle housekeeping does not increase human activity.

## Verification

- `node scripts/check-docs-governance.mjs` — passed.
- `node scripts/check-golden-principles.mjs` — passed.
- `git diff --check` — passed before staging; the final staged check is part of the commit proof.

No product code, dogfooding formula, fixture classifier, account filter, or canonical status
document is changed by this report.
