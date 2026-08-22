# ADR-0056: Direct Human Board Editing Is First-Class; the Proposal Loop Governs Non-Human Actors

- **Status**: Accepted
- **Date**: 2026-08-22
- **Deciders**: Chris0Jeky (maintainer ruling on `#1945`, 2026-08-22); recorded by the implementing agent under the ADR-0051 autonomous-admission lane
- **Related**: `#1945` (the skin-porting gap that forced the question), ADR-0003 (Proposal-First Automation / review-first safety), ADR-0038 (Paper UI is canonical), ADR-0017 (Agent tool registry — review-first by default), ADR-0045 (LLM transcript triage)

## Context

Taskdeck's most-repeated invariant is "no silent or destructive mutations": input becomes an
evidence-linked **proposal**, a human approves it, and only then does the board change (ADR-0003).
It is written in `CLAUDE.md`, in `GOLDEN_PRINCIPLES.md`, and in the product copy.

A dogfooding sweep on 2026-08-22 found that a user on a fresh board could not put a card on it. The
entire Paper board surface offered five buttons — "Capture here", "Review", and one "+ capture" per
column — and "+ capture" navigated *away* from the board to `/workspace/inbox`, where the composer
says: *"Captures land in Inbox. Linking to a board creates a proposal, not a card."* No add-card, no
column rename, no column delete, no board settings. The multi-step tour was the only path to a card.

That reads like the invariant being enforced against the owner of the board. It was not. Verified
against the code at `69ac993af`:

- **The endpoints have always been plain authorized CRUD.** `CardsController`
  (`POST` / `PATCH` / `{id}/move` / `DELETE`), `ColumnsController`
  (`POST` / `PATCH` / `DELETE` / `reorder`) and `BoardsController` (`PUT`, `DELETE`) are `[Authorize]`
  and go straight to the service layer. No proposal, no approval, no idempotency ceremony.
- **The client layers have always been complete.** `api/cardsApi.ts`, `columnsApi.ts`,
  `boardsApi.ts` are 1:1 with those endpoints, each with passing specs under `src/tests/api/`;
  `store/board/cardStore.ts`, `columnStore.ts` and `boardCrudStore.ts` expose them through the
  `boardStore` facade.
- **The Legacy skin has always been wired to them.** `ColumnLane.vue` (inline new-card form),
  `ColumnEditModal.vue`, `BoardSettingsModal.vue` via `BoardDialogHost.vue`, `useBoardDragDrop.ts`,
  and `BoardView.vue`'s add-column form.

The canonical skin simply never imported any of it. `views/paper/PaperBoardView.vue` called
`moveCard`, `createColumn` and the shared `CardModal`, and nothing else; `BoardSettingsModal.vue`
and `ColumnEditModal.vue` appeared nowhere under `views/paper/`. The `n` new-card shortcut was
explicitly gated off under Paper (`standardBoardOnlyShortcutsEnabled = !paperOn`) because the DOM
hook it clicks did not exist there.

So the observed behaviour was a **skin-porting gap**, not a policy. But it was indistinguishable
from a policy from the outside, and nothing written down said which it was. The maintainer's ruling
on 2026-08-22 settled it: *a human — especially the primary user — must always be able to modify the
board directly, however they want; the proposal loop is not a gate on humans.*

This ADR records the boundary that was already implemented. It does not create one.

## Decision

**The actor decides the lane, not the operation.**

### 1. Direct human edits are first-class

A signed-in human acting on a board they may write to edits it **directly**: the write happens
immediately, the UI reflects it immediately, and no proposal is created. This covers creating,
renaming, moving and deleting cards; creating, renaming, reordering and deleting columns; and
renaming or archiving the board.

Immediate feedback from a direct action is a requirement, not a nicety — the maintainer's words
were "humans should always get feedback directly from their actions". A direct control that silently
produced a proposal for later review would violate this decision even though it writes nothing
dangerous.

### 2. The evidence-linked proposal loop governs non-human actors

Transcripts, captures, MCP tools, automations and LLM triage keep the full ADR-0003 treatment:
proposal → evidence → explicit human approve → explicit execute. The "no silent mutations"
invariant is about **actors who are not the user**, and it is undiminished by this ADR. Nothing
here relaxes any agent-facing gate; the MCP server stays write-gated and the review lane stays the
only door for agent-originated change.

### 3. Every skin must expose the whole direct-edit capability set

Paper is canonical (ADR-0038). A canonical skin that cannot do what the product does is not a skin
choice, it is a regression, and it is invisible to API-level tests — `src/tests/api/*` were green
for the entire life of this gap. Capability parity between skins is therefore an enforced property,
not an intention: `src/tests/views/paper/boardMutationCapabilityParity.spec.ts` walks each skin's
component import graph and fails if any board-mutation action on the `boardStore` facade is
unreachable from either one.

### 4. Scope of "direct"

Direct-edit authority is exactly write authority on the board, decided server-side:

- the board **owner**, or
- a user with a `BoardAccess` grant whose role can write (`AuthorizationService.CanWriteBoardAsync`;
  `UserRole.Viewer` cannot).

There is no separate "direct edit" permission and this ADR does not introduce one. Collaborator
boards behave exactly like owned boards for anyone who may write to them. A **Viewer** gets no
direct-edit authority; their attempts are refused by the server.

*Measured limitation, recorded honestly:* neither skin currently hides its edit controls from a
Viewer. `BoardDto.CanWrite` is surfaced to the client but only the Paper Inbox composer and triage
table consume it; the board surfaces do not, in either skin. A Viewer therefore sees controls that
the server will refuse. That is a pre-existing Legacy behaviour that the Paper port now mirrors
rather than diverging from — correctness is enforced, affordance is not. Tracked as follow-up work,
not fixed here.

### 5. Provenance and attribution of direct edits

The **write** is per-user throughout: `TryGetCurrentUserId` on every controller, claims-first
identity (ADR-0002), and client-supplied identity fields rejected. There is no anonymous path to a
direct board edit.

The **audit trail is weaker than that, measured at `69ac993af`**, and this ADR does not pretend
otherwise:

Every row below was read off the `SafeLogAsync` call sites at `69ac993af` — `CardService.cs`,
`ColumnService.cs` and `BoardService.cs`. The helper's `userId` parameter is optional and defaults
to `null`, so a call that does not pass it writes an unattributed row:

| Operation | Audit row | Actor recorded |
| --- | --- | --- |
| Board created | `Created` | yes (`ownerId`, `BoardService.cs:265`) |
| Card updated | `AuditAction.Updated` | yes (`actorUserId`, `CardService.cs:164`) |
| Card created / moved / deleted | `Created` / `Moved` / `Deleted` | **no** (`userId` defaults to `null`) |
| Column created / updated / deleted / reordered | `Created` / `Updated` / `Deleted` | **no** |
| Board updated / archived / unarchived | `Updated` / `Archived` / `Unarchived` | **no** (`BoardService.cs:312`, `:314`, `:316`, `:362`) |

Board **create** is the only board-level write that stamps an actor. An earlier draft of this table
claimed "Board updated — yes"; that was wrong, and correcting it is the point of recording
measurements rather than assertions.

So a direct edit is *authorized* per user but, for most operations, not *attributed* in the audit
log. Single-user local-first use has not felt this; a collaborator board would. Stamping the acting
user on the remaining `SafeLogAsync` calls is the obvious fix, is a backend change, and is
deliberately out of scope for the UI port that prompted this ADR. It is tracked separately as
[#1960](https://github.com/Chris0Jeky/Taskdeck/issues/1960).

The claim this ADR makes is therefore precise: direct human edits are **immediate**, **authorized
per user**, and **first-class**. Full audit attribution is a stated goal with a measured gap, not a
property to cite as already true.

### 6. Recorded direction, not a commitment: the post-hoc enhancer

The maintainer proposed inverting the capture machinery for this lane: rather than gating the human
write, the triage/extraction machinery could run **after** a direct edit as an optional
"auto-clean / auto-fix" pass — take what the human typed, tidy it, fill in what a capture would have
extracted (due dates, labels, links), and show them what it did.

This is **recorded as a direction, and explicitly not decided**. Nothing in it is committed to,
scheduled, or designed here. If it is built, two constraints from this ADR bind it:

1. It must not delay or gate the human's write. The direct edit lands first, on its own.
2. Its tidy-up is a *suggestion about a human's work*, so it goes through a lightweight approval
   surface rather than mutating in place — which keeps it consistent with §2 rather than an
   exception to it.

A future ADR should settle its trigger, its surface, and whether "lightweight approve" is the
existing review lane or something smaller.

## Alternatives Considered

**Route direct human edits through the proposal loop.** Rejected by the maintainer's ruling, and it
was never what the code did — adopting it would have meant *removing* working Legacy functionality
and building an approval step for the owner's own clicks. It also fails the immediate-feedback
requirement in §1 by construction.

**Leave the boundary undocumented and just port the UI.** Rejected. The gap was mistaken for policy
by an experienced reader looking at the running app, and the product copy actively reinforced the
misreading ("Linking to a board creates a proposal, not a card"). An undocumented boundary that
already misled someone will mislead again, and the next agent to touch a skin needs to know that
omitting these controls is a regression rather than a safety measure.

**Make Paper stricter than Legacy — a read-only-ish board surface, capture-first by design.**
Rejected. It contradicts the ruling, and it would make ADR-0038's "Paper is canonical" false in
practice: the canonical skin would be the less capable one.

**Gate the new Paper controls on `BoardDto.CanWrite` as part of this change.** Rejected *for now*
(§4). It is the right end state, but doing it in Paper alone would make the two skins diverge in a
second way while fixing the first, and it belongs with the audit-attribution work.

## Consequences

**Positive**

- The canonical skin can do what the product does. Direct add-card, column rename/reorder/delete and
  board settings are reachable from the Paper board without leaving it, and the `n` shortcut works
  in both skins off one DOM contract.
- The invariant is stated in a form that survives contact with reality: "no silent mutations by
  non-human actors" is defensible and enforced; "no direct board writes" was neither true nor
  desirable.
- A future skin cannot repeat the omission quietly — the parity guard fails with the missing
  capability names.

**Negative / accepted costs**

- The two lanes now sit side by side in one column footer (`+ card` and `+ capture`). Users have to
  learn which is which. Mitigated by hierarchy — `+ card` is the primary control, `+ capture` a
  small secondary link — but it is genuinely two doors where there was one.
- The audit-attribution gap in §5 is now written down and therefore owed. It was always there; this
  ADR makes it a debt with a name.
- The Viewer-affordance gap in §4 is likewise recorded rather than fixed.
- `+ capture` copy still frames the proposal lane as the way onto a board. It is now one of two, and
  reads as more authoritative than it should.

**Neutral**

- No backend, API-client or store change was needed to implement any of this, which is the strongest
  evidence that the boundary described here was the one already built.
- Board "delete" remains archive. `DELETE /api/boards/{id}` calls `board.Archive()`; the UI says
  "Move to archive" in both skins because that is what happens. This ADR does not introduce a
  permanent board delete.

## References

- `#1945` — Paper board management: the maintainer ruling and the verified diagnosis
- ADR-0003 — Proposal-First Automation (Review-First Safety)
- ADR-0038 — Paper UI Is the Canonical Frontend (Legacy Frozen)
- `frontend/taskdeck-web/src/tests/views/paper/boardMutationCapabilityParity.spec.ts` — the enforced
  half of §3
- `frontend/taskdeck-web/src/tests/views/paper/PaperBoardManagement.spec.ts` — direct-lane behaviour
  from the Paper surface
- `backend/src/Taskdeck.Application/Services/{CardService,ColumnService,BoardService}.cs` — the
  audit-attribution measurements in §5
