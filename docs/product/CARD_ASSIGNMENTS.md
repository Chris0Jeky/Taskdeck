# Card assignments

Assignments express responsibility for an existing card. They do not grant access,
change board membership, log work, estimate effort, or turn an assignee hint into an
identity. The foundation uses the existing User and Card model under
[ADR-0060](../decisions/ADR-0060-canonical-work-model-and-compatibility-path.md).

## Eligibility and editing

An eligible person is an active user who owns the board or has a board-access row.
Owners need no access row; Viewers can be assigned but retain their read-only role.
The board-scoped participant endpoint returns IDs and display names, without email
addresses, invitations, or a global user directory.

The shared Paper inspector and Legacy card editor show the current assignees and
offer a multiple-selection control, Clear, Cancel, and Save assignments. Other card
drafts survive assignment saves. A submitted set replacement cannot be recalled, so
while one is in flight the editor never offers a discard: closing, Escape, the
backdrop, the header close, switching card and leaving the board all answer that the
change was already sent and must be waited out, and an open discard confirmation is
withdrawn. Settlement restores every control and shows either the committed assignees
or the failure with the kept draft. An uncertain save or version conflict keeps the
selection, requires a current-state refresh, and leaves the retry explicit. Removed
participants remain visible in the draft so the user can remove them. Archived
cards and boards are read-only; historical assignments remain visible.

Direct human API commands use:

- `GET /api/boards/{boardId}/participants`: authorized board-scoped projection.
- `PUT /api/boards/{boardId}/cards/{cardId}/assignments`: write-authorized set
  replacement with `userIds` and `expectedUpdatedAt`.

Both command properties are required. Omission never means clear; `userIds: []`
explicitly clears. Duplicate IDs normalize to a set. Every requested ID must be
eligible, or the whole command fails. A stale card timestamp returns Conflict.
Retained rows preserve their assignment time and acting user; a no-op set preserves
the card version. Card responses include `assignments` with `userId`,
`displayName`, `assignedAt`, and `assignedByUserId`.

## Proposal and MCP boundary

The `card / replace-assignments` operation carries `cardId`, `userIds`,
and `expectedUpdatedAt`. Preview names the before/after assignee sets. Apply
revalidates active card/board state, eligibility, authorization and version.
Approval and Apply remain separate explicit actions. Acting-user attribution comes
from the server's executing principal, never the operation JSON.
An assignment replacement is the only operation on its card in a proposal; other
cards may have their own operations. This keeps the reviewed card version valid
through Apply instead of approving two conflicting writes to the same version.

MCP card resources and search results expose assignments. The read-scoped
`list_board_participants` tool supplies eligible choices; the propose-scoped
`replace_card_assignments` tool creates a proposal and never mutates a card.
Capture `AssigneeHint` values are not interpreted as user IDs.

## Serialization, erasure and audit

`CardAssignments` has the composite primary key `(CardId, UserId)`, an assignment
timestamp and acting-user reference. Direct replacement and access revocation
acquire the existing SQLite write transaction before authoritative reads.
Unchanged preflight entity snapshots are reloaded/discarded inside that boundary.
Proposal execution and account deletion reuse their existing outer transactions;
they do not start nested transactions. Card timestamps and the existing board
concurrency token also protect competing card/board writes.

Revoking a participant's access detaches assignments only when ownership no longer
keeps them eligible. Account erasure removes that user's assignments, including
archived cards, before anonymization and reports `cardAssignmentsRemoved`.
Assignment audit reasons are `assignment-replace`, `access-revoked`,
`account-erased`, and `assignment-import-mapping`. Assignment effects and audit
rows commit together; realtime invalidation happens after commit. When a proposal
is applied, both the `assignment-replace` row and the generic execution-history row
name the authenticated applying user; the requester is named in the
execution-history row's provenance text instead.

## Board JSON and account portability

Buffered and streamed account exports include assignments on all authorized cards,
including archived cards, using the same existing board scope.

Board JSON with assignments uses the `taskdeck-board` version-4 envelope. Its
top-level shape intentionally prevents older importers from silently dropping
assignments. Assignment-free plain exports and version-2/3 envelopes remain
compatible.

The import screen sends the source JSON to
`POST /api/import/boards/preview`. The same parser and import validation used by
Apply are exercised in a rolled-back transaction: no board, card, assignment, audit
row, or notification survives preview. Preview returns the parsed board and every
distinct source person with their affected-card count.

Imports create a **new board with fresh card IDs**. The authenticated importer is
its only initial eligible participant. Every source key must explicitly map to the
importer ("Me") or null ("Unassigned"). Names, email addresses and source UUIDs
never match automatically; memberships are never imported. Multiple source people
mapped to Me collapse to one assignment per card. Source attribution describes the
old file; the new assignment is attributed to the importing user at import time.
This is a mapped copy, not identity-preserving restore.

The typed endpoint accepts `sourceAssignees: [{sourceKey, displayName}]` on import
cards and an `assigneeMappings` object on the import board. The raw JSON endpoint
accepts `{source: <original JSON object>, assigneeMappings: {...}}`. Both routes
reject missing mappings, unknown source keys and targets other than the importer
or null, and revalidate the submitted payload atomically. Preview never authorizes
an altered or now-invalid Apply payload.

## Migration and rollback

`AddCardAssignments` is additive. Existing cards have an empty assignment set;
there is no single-assignee field to backfill. Card IDs, placement, hierarchy and
history are preserved. Down drops only the assignment table: assignments are lost,
while cards, hierarchy and users remain. Keep the normal pre-migration database
backup if the assignment data must be recoverable. Version-4 board files require
an assignment-aware importer.

## Proof entry points

- `CardAssignmentTests`: set identity, retained metadata, no-op and archived cleanup.
- `CardAssignmentApiTests`: permissions, eligibility, direct/proposal/MCP parity,
  explicit mapping, export scope, audit rollback and post-commit observation.
- `CardAssignmentConcurrencyTests`: separate SQLite scopes, stale preflight
  snapshots and both assignment/revoke/account-erasure/archive commit orders.
- `MigrationBootstrapTests.AssignmentMigrationStartsEmptyAndDownPreservesCardsAndHierarchy`.
- Frontend `CardAssignmentField.spec.ts`, `useCardModal.spec.ts`,
  `ExportImportView.spec.ts`; real-API Chromium `card-assignments.spec.ts`.
- In-flight save honesty: `CardModalAssignmentSave.spec.ts` (Paper and Legacy,
  delayed success and failure, Escape/backdrop/header/confirmation) and the
  card-switch, route-leave and unload refusals in `PaperBoardView.spec.ts`.
