# Card estimates and current totals

Cards can have an optional estimate in whole minutes, displayed as hours and minutes. Enter it
in card details or during Paper/Legacy quick-create. Both fields blank means **Not estimated**;
entering zero records a known **0m** estimate. Clear both fields and save to remove an estimate.
Minutes must be 0–59 and the total must be at most 1,000,000 minutes (16666h 40m). Existing cards
remain valid without an estimate. Archived cards retain estimates and become editable after restore.

Open **Estimates** on a board to see active-card counts, known estimated effort and missing-estimate
counts for the board, each column, each current participant and unassigned cards. Board readers,
including Viewers, can read these totals. Assignment never grants access; participants use the
existing board-owner and board-access boundary.

Each card contributes once to the board and its current column. A card assigned to several people
contributes its full estimate to each person, so participant totals overlap and must not be added
to obtain a board total. Parent and child estimates are independent. Known zero contributes no
minutes but reduces the missing count. Archived cards are excluded. These are current assignment
and estimate totals, not historical activity, time worked, availability or capacity forecasts.

Totals are derived on request, with a snapshot timestamp. Board changes mark a displayed snapshot
stale; use **Refresh estimates** to load current totals. A failed read exposes an explicit retry.
Escape while focused on **Refresh estimates** closes the panel and returns focus to **Estimates**.
These totals require a connected backend. The backend-less demo currently reports a read error;
demo availability is tracked in [#3056](https://github.com/Chris0Jeky/Taskdeck/issues/3056).

## Writes and automation

The API field is nullable `estimatedEffortMinutes`, a whole number from 0 through 1,000,000.
Create omission/null means unknown. On update, omission/null leaves the saved estimate unchanged;
`clearEstimatedEffort: true` removes it. A value together with the clear flag is invalid.
Intentional set/clear operations require the caller's `expectedUpdatedAt` version. Unrelated old
client updates retain their existing behavior and do not erase estimates. Stale writes fail with
a conflict before committing changes. A successful editor receipt advances its version while
preserving a newer unsaved draft.

Proposal previews show estimate changes in hours/minutes and retain explicit approval and Apply.
Existing-card estimate operations pin the initial card version; ordered operations then use the
transaction's current tracked version. Cards created earlier in the same proposal need no initial
persisted version. A competing write rolls the proposal transaction back, including earlier saves.
Audit and proposal provenance retain the existing actor-attribution policy.

Chat and MCP tools expose optional `estimated_effort_minutes`, `clear_estimated_effort` and
caller-supplied `expected_updated_at` where applicable. They create proposals through the existing
review flow. MCP `get_board_estimate_rollups` requires Read scope plus board read authority;
`GET /api/boards/{boardId}/estimate-rollups` uses the same service. Aggregate minutes use a 64-bit
integer so large boards do not overflow a card's integer range.

## Portability and lifecycle

Current board JSON exports/imports and buffered/streaming account exports preserve null, zero and
positive estimates, including archived cards. Older files without the field import as unestimated.
Malformed or out-of-range import values are rejected before records are added or saved. Existing
assignment mapping, hierarchy remapping, deletion and account-erasure behavior remains in force.
No new principal, participant, estimate-history or aggregate table is introduced.

Migration `20260912155107_AddCardEstimatedEffort` adds one nullable card column. Its developer Down
path removes estimate metadata while retaining cards; reapplication cannot restore erased values.
See [upgrading and backup guidance](../../UPGRADING.md). Physical-device, screen-reader and release
acceptance remain separate in [OUTSTANDING_TASKS](../../OUTSTANDING_TASKS.md).
