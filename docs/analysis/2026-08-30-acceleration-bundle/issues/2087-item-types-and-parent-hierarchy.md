# WM-TYPE — Minimal item types and optional parent hierarchy (#2087)

Last Updated: 2026-09-02

> Curated from the v0.3/v0.4 acceleration bundle (grounded `221aa88c8`, 2026-08-30) against `main` `de488fea0` on 2026-09-02 under tracker #2348 follow-up. Planning input, not authority: the live issue, the accepted ADRs and `docs/STATUS.md` win. Corrections to the bundle are listed in the last section.

## Outcome

Give a card an admitted work-item type (Task / Epic / Spike, default Task) and at most one optional
same-board parent, with a server-side cycle check and a hard depth cap — without changing card
identity, board/column placement, authority, or the review-first mutation path.

## Live dependencies (verified 2026-09-02)

| Issue / artefact | State | What it must supply first | Blocks |
| --- | --- | --- | --- |
| ADR-0060 `docs/decisions/ADR-0060-canonical-work-model-and-compatibility-path.md` | **Accepted** (line 3: "Status: Accepted (ratified … 2026-08-29 …)") | `first-item-types` = Task/Epic/Spike, `hierarchy-boundaries` = same board / one parent / depth cap 3 / server-side cycle check / type-agnostic, `parent-lifecycle` = detach-never-cascade, `compat-path` stages 1–3 | nothing — the blocker is gone |
| `#2185` archive-card proposal operation | **open**, v0.3 | A real card-archive state. `Card.cs` has **no** archive field (`IsBlocked` / `BlockReason` only), and `OperationHandlerRegistry.ArchiveCardAsync` still maps "archive" onto `Block` | the **archive** half of `parent-lifecycle` only (cascade-archive slice). Delete-side detach is not blocked |
| `#2187` architecture review (multi-board identity + hierarchy boundaries) | **open**, no milestone | Nothing. ADR-0060 records that `hierarchy-boundaries` = A *holds until* that review is recorded as an ADR amendment | nothing today; it is a revisit trigger, not a gate |
| `#2084` decision issue | **closed** | Already delivered ADR-0060 | nothing |

Nothing in the repository references `WorkItemType` or `ParentCardId` (0 files, grepped across
`backend/src` and `frontend/taskdeck-web/src`). No hierarchy code exists on `main`.

## Child slices (one PR each, in order)

| Id | Outcome | Depends on | Mode | Startable before predecessors merge? |
| --- | --- | --- | --- | --- |
| `WM-TYPE-01-contract` | `WorkItemType` enum, `Card.ParentCardId`, additive migration + down path, pure hierarchy validator, `CardDto`/`frontend types/board.ts` fields | — | contract-only | **Yes — this is the startable-now slice.** It needs no undelivered predecessor; ADR-0060 already fixes every value it encodes |
| `WM-TYPE-02-commands` | Direct-human `set-type` / `set-parent` / `detach-parent` through `CardService`, sharing one validator with proposal preview and apply | 01 | implementation | No — 01 owns `Card.cs` and the EF snapshot |
| `WM-TYPE-03-delete-detach` | Deleting a parent detaches children, audited per child, one `boardMutation` event per affected card | 02 | implementation | No |
| `WM-TYPE-04-export` | Parent/type round-trip. Requires a card-level stable key in the board JSON contract — `ImportCardDto` has none today | 01 | implementation | No — it is a change to a shipped serialization contract |
| `WM-TYPE-05-archive-cascade` | Confirmed cascade-archive whose preview names every affected id/count | 03, **`#2185`** | implementation | **No — blocked.** There is no card-archive state to cascade |
| `WM-TYPE-06-ui` | Type selector, parent picker restricted to valid same-board candidates, breadcrumb, explicit detach/cascade confirmation | 02 | implementation | No |

## Architecture

| Concern | Type | Status | Note |
| --- | --- | --- | --- |
| Work item identity | `Card : Entity` (`Guid Id`, `BoardId`, `ColumnId`, `Title`, `Description`, `DueDate`, `IsBlocked`, `BlockReason`, `Position`, `CardLabels`) | **exists** | The full field list on `main`. No type, no parent, no archive flag, no assignee |
| Board scope + ownership | `Board.OwnerId` is `Guid?` (**nullable**) | **exists** | "Same owner" is not a safe equality check while `OwnerId` can be null on both sides. Scope on `BoardId`, which is non-null |
| Optimistic concurrency | `Board.ConcurrencyToken` + `Board.RecordCardMutation()`; `UpdateCardDto.ExpectedUpdatedAt` | **exists** | Cards carry **no** row version. Reparent must join the same conditional board update ADR-0063 / `#2114` established |
| Archived-board write protection | `CardService` → `ErrorCodes.InvalidOperation` → `409` (ADR-0063) | **exists** | A reparent is a card write and inherits this contract for free |
| Proposal operation dispatch | `OperationHandlerRegistry.ExecuteCardOperationAsync` — `switch (actionType)` over `create` / `update` / `move` / `archive` plus the label vocabulary | **exists** | Vocabulary is `(targetType, actionType)` **string pairs**, not a dotted `work-item.*` namespace |
| Preview == Apply | `ProposalOperationContractValidator.ValidateAsync` (`#1319`) | **exists** | The one place preview and apply share. A new action must be added here *and* in the registry in the same PR |
| Create-time operation shape | `ProposalOperationInputValidator` — permissive `\A[A-Za-z][A-Za-z0-9_.-]*\z` token regex, deliberately **not** an allowlist | **exists** | An unknown action is accepted at create and preview and fails only at Apply. Do not widen that gap |
| Error vocabulary | `ErrorCodes` in `Domain/Exceptions/DomainException.cs` — 15 PascalCase constants; `ResultExtensions.ToHttpStatusCode` maps anything unknown to **500** | **exists** | The bundle's `work_parent_cycle`-style codes would surface as 500s |
| Realtime | `BoardRealtimeEvent(BoardId, entityType, action, entityId, timestamp)` on `boardMutation`, group `board:{id:N}` (`BoardHubGroups.ForBoard`) | **exists** | Per-board, generic. Detach fan-out means one event per child unless a batch shape is added |
| Board JSON export/import | `ExportBoardDto` uses `CardDto` (has `Id`); `ImportBoardDto` uses `ImportCardDto(Title, Description, ColumnName, Position, DueDate, Labels)` — **no Id**, column matched by **name** | **exists** | ADR-0060 already records that board JSON mints fresh ids. Parent references therefore have nothing to point at |
| `WorkItemType`, `Card.ParentCardId`, hierarchy validator | — | **new** | Domain-pure validator, no EF dependency, so it is unit-testable in `Taskdeck.Domain.Tests` |

**Depth convention is an open decision.** ADR-0060 says "a hard depth cap of 3" and does not say
whether 3 counts nodes or edges. The bundle candidate counts **nodes** (a root alone is depth 1, so
3 = grandparent → parent → child, two edges). Record the convention in the issue and in the test
names before writing the validator; the blueprint itself demands "tests must spell out levels".

## Implementation plan

**Preflight.** Read `#2087`'s two comments (the 2026-08-29 ruling comment and the 2026-08-29
milestone move) and ADR-0060 "Decisions recorded (2026-08-29)". Confirm `#2185` is still open before
planning anything on the archive side.

**Sequence.** 01 → 02 → 03, then 04 and 06 in parallel; 05 last and only after `#2185`.

**Producer-owned paths (01).** `backend/src/Taskdeck.Domain/Enums/WorkItemType.cs` (new),
`backend/src/Taskdeck.Domain/WorkModel/` (new, the pure validator),
`backend/tests/Taskdeck.Domain.Tests/WorkModel/` (new).

**Integration-owner seams — one owner across `#2087`/`#2092`/`#2093`/`#2094`/`#2240`:**
`Domain/Entities/Card.cs`, `Application/DTOs/CardDto.cs`, `Application/DTOs/AuditAndExportDtos.cs`,
`Application/Services/CardService.cs`, `Application/Services/Pipeline/OperationHandlerRegistry.cs`,
`Application/Services/Pipeline/ProposalOperationContractValidator.cs`,
`Infrastructure/Migrations/TaskdeckDbContextModelSnapshot.cs`,
`frontend/taskdeck-web/src/types/board.ts`.

**Rollout / rollback.** Stage 1 ships the column with default `Task` and a null parent — a pure read
compatibility change. Server validators land before any write control is exposed. Keep the tested
down migration until a release containing hierarchy writes is cut (ADR-0060 stage-2 requirement).

**Definition of done.** Every ADR-0060 cross-cutting clause proven, not asserted: permissions,
proposal diff/apply, audit and attribution, export/import, account deletion, MCP/API compatibility,
realtime invalidation, optimistic concurrency, migration bootstrap, rollback.

## Test plan

- [ ] Domain: self-parent, two-node cycle, long cycle, pre-existing corrupt cycle (visited-set fail-closed), depth at the cap, depth one past the cap, reparenting a **subtree** whose height pushes the result past the cap — `dotnet test backend/tests/Taskdeck.Domain.Tests/Taskdeck.Domain.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~Hierarchy"`
- [ ] Domain: cross-board parent rejected on `BoardId` inequality (not on owner, which is nullable)
- [ ] Application: the same validator instance runs from `CardService` and from `ProposalOperationContractValidator`, and Apply re-validates against current state rather than trusting preview
- [ ] Application: an unknown `actionType` on a card operation is rejected at **preview**, not only at Apply (closes the gap the permissive input regex leaves)
- [ ] Application: deleting a parent detaches every child, emits one audit row per child and one `BoardRealtimeEvent` per affected card
- [ ] Api: cycle / depth / cross-board each return a stable code from the existing `ErrorCodes` set (`Conflict` → 409 or `ValidationError` → 400 — decide once, test both paths) — `dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release -m:1`
- [ ] Integration: migration from an empty database and from a populated one; every pre-existing card reads back as `Task` with a null parent and an unchanged `Id`; down migration exercised — `--filter "FullyQualifiedName~MigrationBootstrap"`
- [ ] Integration: a reparent racing a board archive rolls back with `409` (ADR-0063 conditional board update)
- [ ] Export: parent/type round-trip — this test cannot pass until `ImportCardDto` carries a card key; the slice must add one or record the limitation explicitly
- [ ] Architecture: `dotnet test backend/tests/Taskdeck.Architecture.Tests/Taskdeck.Architecture.Tests.csproj -c Release -m:1`
- [ ] Frontend: `cd frontend/taskdeck-web; npx vitest --run --maxWorkers=2 <spec>` for the type selector and parent picker
- [ ] Docs: `node scripts/check-docs-governance.mjs`

## Edge cases

- Self-parent; two-node cycle; a long cycle; a cycle that already exists in the data (must fail closed, not loop).
- Depth exactly at the cap and exactly one past it, under whichever convention is recorded.
- Reparenting a node that **has descendants** — the resulting subtree height, not just the moved node, must fit under the cap.
- Two concurrent reparents of the same child, and a reparent racing a board archive.
- Parent deleted between validation and commit.
- Import order child-before-parent (unresolved references must be reported, never silently dropped).
- Down migration executed while hierarchy rows exist.
- A card whose parent lives in a column that is deleted — parent is board-scoped, column moves must not disturb it.
- An Epic parented under a Spike: **allowed**, because `hierarchy-boundaries` is type-agnostic. Test it so nobody "fixes" it later.

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| C# candidate | `docs/analysis/2026-08-30-acceleration-bundle/candidates/dotnet/WorkHierarchyRules.cs` | The shape of the validator: self-parent, scope, cycle with a visited set, and *subtree height* added to parent depth | Node record carries `OwnerUserId` and `IsArchived`, neither of which exists on `Card`; snake_case error codes are not in Taskdeck's `ErrorCodes`; `GetSubtreeHeight` scans all nodes per level and cannot tell "no children" from "children not loaded" |
| C# candidate tests | `.../candidates/dotnet/tests/WorkHierarchyRulesTests.cs` | Case names worth keeping | Compile against the candidate namespace, not Taskdeck |
| Test vectors | `.../testing/test-vectors/hierarchy-cases.json` | The ten case **names** and the expected-error labels | Names only — there are no node fixtures, so it is a checklist, not a vector file |
| Diagram | `.../diagrams/work-model.svg` | Explaining that hierarchy, links, assignment and fields are four separate models | Explanatory only; it omits `depends-on` from the link list |
| Blueprint | `.../architecture/WORK_MODEL_IMPLEMENTATION_BLUEPRINT.md` §2, §4, §8 | Table shape, hierarchy rules, export ordering | See its validation preface |

## Corrections to the bundle

1. **Bundle pack says** the recommended state is `ready-after-contract-freeze` and depends on
   nothing. **True on `main`:** correct for the type/parent half, but the `parent-lifecycle`
   **archive** half has a live prerequisite the pack never names — ADR-0060's own text: "the shipped
   archive-card proposal operation applies as a silent no-op today (`#2185`), so a real card-archive
   state and handler must exist and be proven before child behavior is defined on top of it."
   **Consequence:** the cascade-archive slice is blocked; split it out rather than blocking the issue.
2. **The `#2087` ruling comment (2026-08-29) says** `ArchiveCardAsync` builds an `UpdateCardDto` with
   "a null `BlockReason`, which `CardService.UpdateCardAsync` ignores". **True on `main`:** the
   no-op was fixed by commit `c7f865674` ("fix(proposals): make archive card outcome visible", merged
   in PR `#2216`) — the handler now passes `OperationHandlerRegistry.ArchiveCardBlockReason`
   ("Archived by an approved proposal.") so `card.Block(reason)` runs. **Consequence:** the operation
   is no longer silent, but it still maps archive onto *blocked*; `Card` has no archive state, so the
   substantive `#2185` acceptance is unmet and the prerequisite stands for a different reason than
   the comment gives.
3. **Bundle:** "Enforce Task/Epic/Spike, same-board parent, maximum depth 3" as if the convention
   were settled. **True:** ADR-0060 says "a hard depth cap of 3" with no node/edge convention, and
   the candidate silently chooses nodes. **Consequence:** record the convention in the issue before
   the validator is written.
4. **Bundle:** the validator rejects a parent whose `OwnerUserId` differs. **True:** `Card` has no
   owner field and `Board.OwnerId` is `Guid?`. **Consequence:** scope on `BoardId` equality only;
   an owner comparison between two nulls would silently pass.
5. **Bundle:** suggests the operation vocabulary `work-item.set-type` / `work-item.set-parent` /
   `work-item.detach-parent` / `work-item.archive-subtree`. **True:** dispatch is on
   `(targetType, actionType)` — `targetType == "card"` and a bare verb — and
   `ProposalOperationInputValidator` accepts any identifier-like token, so a dotted name would be
   *stored* happily and then fail at Apply. **Consequence:** either extend the card verb set
   (`set-parent`, `detach-parent`, `set-type`) or introduce a new `targetType`, and add the verb to
   the contract validator and the registry in the same PR.
6. **Bundle:** error codes such as `work_parent_cycle`, `work_parent_depth_exceeded`. **True:**
   `ErrorCodes` is a closed 15-member PascalCase set and `ToHttpStatusCode` maps anything else to
   **500**. **Consequence:** map hierarchy failures onto `ValidationError` (400) or `Conflict` (409)
   and carry the detail in the message, or extend `ErrorCodes` deliberately with its HTTP mapping.
7. **Bundle §8 export order** assumes "ID remapping must be deterministic and recorded". **True:**
   `ImportCardDto` carries no id at all and resolves its column by **name**; the importer constructs
   `new Card(board.Id, column.Id, …)`. **Consequence:** there is nothing for a parent reference to
   remap *to*. Round-tripping hierarchy requires adding a card-level key to the board JSON contract —
   a shared-contract change, not a `#2087` detail.
8. **Bundle:** "concurrency token/version participates in reparent commands". **True:** cards have no
   row version; the shipped mechanisms are `UpdateCardDto.ExpectedUpdatedAt` and the ADR-0063
   non-advancing board marker (`Board.RecordCardMutation`). **Consequence:** reuse those two, do not
   add a third.
9. **Bundle pack:** "Unblocks: #2092, #2093". **True:** `#2092` (typed links) shares only the
   contract-freeze seam — it needs no `WorkItemType` or `ParentCardId` column — and `#2093`'s live
   predecessor is `#2240`, not `#2087`. **Consequence:** treat the edge as *serialize on the shared
   files*, not as a functional dependency.
