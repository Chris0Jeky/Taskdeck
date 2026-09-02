# WM-LINK — Minimal typed work-item links (#2092)

Last Updated: 2026-09-02

> Curated from the v0.3/v0.4 acceleration bundle (grounded `221aa88c8`, 2026-08-30) against `main` `de488fea0` on 2026-09-02 under tracker #2348 follow-up. Planning input, not authority: the live issue, the accepted ADRs and `docs/STATUS.md` win. Corrections to the bundle are listed in the last section.

## Outcome

Let two cards on the same board carry one of five admitted typed edges — `relates-to`, `blocks`,
`depends-on`, `duplicates`, `spawned-from` — with stable direction, canonical storage that cannot
hold a reverse duplicate, one validator shared by the human command path and proposal apply, and no
new authority: an edge grants nobody access to anything.

## Live dependencies (verified 2026-09-02)

| Issue / artefact | State | What it must supply first | Blocks |
| --- | --- | --- | --- |
| ADR-0060 | **Accepted** | The five edge types (`WorkRelation`, line 41) and `relation-scope` = A (same board only, cross-board endpoint fails server-side) | nothing — the stated dependency is satisfied |
| `#2087` item types + parent | **open**, v0.4 | Nothing functional. Parent is a dedicated `Card` column, not an edge, so this issue does not read or write it | nothing functionally; **shares** the contract-freeze seam |
| `#2187` architecture review | **open** | Nothing. `relation-scope` holds until amended | revisit trigger only |

Nothing in the repository references `WorkItemLink` or any relation table (0 files, grepped across
`backend/src` and `frontend/taskdeck-web/src`). No relation code exists on `main`.

## Child slices (one PR each, in order)

| Id | Outcome | Depends on | Mode | Startable before predecessors merge? |
| --- | --- | --- | --- | --- |
| `WM-LINK-01-contract` | `WorkRelationType` enum, `WorkItemLink` entity + EF configuration + additive migration with the canonical unique index, pure validator/canonicalizer | — | contract-only | **Yes — this is the startable-now slice**, provided the shared-file owner sequences it against `#2087`-01. It is a new table and new files; it needs no `Card` column |
| `WM-LINK-02-commands` | Human create/delete link through a service, sharing the validator with proposal preview and apply | 01 | implementation | No |
| `WM-LINK-03-reads` | Adjacency reads with inverse display labels, audit rows, per-board realtime invalidation | 02 | implementation | No |
| `WM-LINK-04-export` | Round-trip and the dangling-edge policy on card delete and on import. Requires a card key in the board JSON contract | 01 | implementation | No — shared serialization contract |
| `WM-LINK-05-mcp` | Read tools plus proposal-only writes | 02 | implementation | No |
| `WM-LINK-06-ui` | Compact relationship editor, inverse labels, duplicate-target cue | 03 | implementation | No |

## Architecture

| Concern | Type | Status | Note |
| --- | --- | --- | --- |
| Endpoint identity and scope | `Card.Id` (`Guid`, from `Entity`), `Card.BoardId` (non-null `Guid`) | **exists** | Scope on `BoardId` equality. `Board.OwnerId` is `Guid?`, so an owner comparison is not a safe scope test |
| Board-scoped access predicate | `IAuthorizationService.CanReadBoardAsync` / `CanWriteBoardAsync` / `GetWritableBoardIdsAsync` — "the set `BoardAccess.CanWrite()` admits (Owner / Admin / Editor) **plus board ownership**" | **exists** | The owner-or-access predicate the ADRs require. Never check a `BoardAccess` row alone |
| Archived-board write protection | `CardService` → `ErrorCodes.InvalidOperation` → `409` (ADR-0063), enforced by a conditional board update using `Board.ConcurrencyToken` | **exists** | Link writes are board-scoped writes and must join the same conditional update; ADR-0063 already extended it to the bulk writers (`#2114`) |
| Error vocabulary | `ErrorCodes` — 15 PascalCase constants; unknown → **500** via `ResultExtensions.ToHttpStatusCode` | **exists** | `work_link_duplicate` and friends are not in it |
| Proposal dispatch | `OperationHandlerRegistry` — `targetType` is one of `card` / `board` / `column`; anything else returns "Unsupported target type" | **exists** | A `link` target type is a *new* branch in this method, not a config entry |
| Preview == Apply | `ProposalOperationContractValidator.ValidateAsync` (`#1319`), which validates in `Sequence` order and registers cards planned by earlier operations (`RegisterPlannedCard`) | **exists** | A link whose endpoint is created by an earlier operation in the same proposal already has a precedent to follow |
| Realtime | `BoardRealtimeEvent(BoardId, entityType, action, entityId, timestamp)` on `boardMutation` | **exists** | Add `entityType: "link"` rather than inventing a second channel |
| Board JSON import | `ImportCardDto(Title, Description, ColumnName, Position, DueDate, Labels)` — **no id**; importer runs `new Card(board.Id, column.Id, …)` | **exists** | An edge references two cards; with no card key in the import contract there is nothing to attach an edge to |
| `WorkItemLink`, `WorkRelationType`, canonicalizer | — | **new** | Board-scoped root. Store `BoardId` on the edge so scope survives a card move and the unique index stays board-local |

**Canonical storage, not a state machine, is what prevents a reverse duplicate.** `blocks` and
`depends-on` are the two readings of one directed dependency: store one row and render the inverse
label. `relates-to` is symmetric and must be stored with a deterministic endpoint order. Enforce both
with a **unique index**; the C# canonicalizer alone cannot serialize two concurrent writers.

## Implementation plan

**Preflight.** Read `#2092`'s two comments — the `relation-scope` = A ruling and the v0.4 move — and
ADR-0060 lines 38–43 and the `relation-scope` entry. Confirm whether `#2087`-01 has merged; if it
has not, agree the `Card.cs` / DTO / snapshot ordering with the shared-contract owner first.

**Sequence.** 01 → 02 → 03, then 04, 05, 06.

**Producer-owned paths (all new):** `backend/src/Taskdeck.Domain/Entities/WorkItemLink.cs`,
`backend/src/Taskdeck.Domain/Enums/WorkRelationType.cs`,
`backend/src/Taskdeck.Domain/WorkModel/` (the canonicalizer/validator),
`backend/src/Taskdeck.Infrastructure/Persistence/Configurations/WorkItemLinkConfiguration.cs`,
`backend/tests/Taskdeck.Domain.Tests/WorkModel/`.

**Integration-owner seams:** `Infrastructure/Persistence/TaskdeckDbContext.cs`,
`Infrastructure/Migrations/TaskdeckDbContextModelSnapshot.cs`,
`Application/Services/Pipeline/OperationHandlerRegistry.cs`,
`Application/Services/Pipeline/ProposalOperationContractValidator.cs`,
`Application/DTOs/AuditAndExportDtos.cs`, `frontend/taskdeck-web/src/types/board.ts`.

**Rollout / rollback.** The table is additive and empty on arrival, so stage 1 is inert. Expose reads
before writes. Card **delete** must have a defined edge behavior on day one — decide cascade-delete
of incident edges versus tombstone, and prove it, because the FK exists the moment the table does.

**Definition of done.** ADR-0060's cross-cutting clause list in full — permissions, proposal
diff/apply, audit and attribution, export/import, account deletion, MCP/API compatibility, realtime
invalidation, optimistic concurrency, migration bootstrap, rollback.

## Test plan

- [ ] Domain: self-link rejected; both endpoints must share a `BoardId`; a missing endpoint fails closed — `dotnet test backend/tests/Taskdeck.Domain.Tests/Taskdeck.Domain.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~WorkRelation"`
- [ ] Domain: `depends-on(A,B)` and `blocks(B,A)` canonicalize to the **same** stored row; the second request is a duplicate, not a second edge
- [ ] Domain: `relates-to(A,B)` and `relates-to(B,A)` canonicalize identically
- [ ] Domain: a dependency cycle of length 2 and of length ≥3 is rejected; `relates-to` cycles are allowed
- [ ] Domain: `duplicates` and `spawned-from` — decide and test whether a mutual pair and a chain are permitted (the candidate silently permits both)
- [ ] Persistence: the unique index rejects a duplicate under a two-writer race, not just the in-memory scan — `dotnet test backend/tests/Taskdeck.Integration.Tests/Taskdeck.Integration.Tests.csproj -c Release -m:1`
- [ ] Application: preview and apply run the identical validator; apply re-validates against current state and returns a conflict rather than trusting the preview
- [ ] Application: an edge grants no read access — a principal who cannot read the board cannot read the edge or its endpoints
- [ ] Application: deleting an endpoint card resolves incident edges by the recorded policy, with an audit row and a `boardMutation` event
- [ ] Integration: a link write racing a board archive returns `409` (ADR-0063)
- [ ] Integration: migration from empty and from a populated database; down migration exercised — `--filter "FullyQualifiedName~MigrationBootstrap"`
- [ ] Export: round-trip, or an explicitly recorded limitation while `ImportCardDto` has no card key
- [ ] Architecture: `dotnet test backend/tests/Taskdeck.Architecture.Tests/Taskdeck.Architecture.Tests.csproj -c Release -m:1`
- [ ] Docs: `node scripts/check-docs-governance.mjs`

## Edge cases

- Self edge; an edge to a card on another board; an edge to a card that does not exist.
- `A blocks B` submitted while `B depends-on A` exists — one row, not two.
- Two concurrent identical creates (the index, not the validator, is the guard).
- A dependency cycle introduced by the edge being added, and a cycle that already exists in data.
- A card moved to another column (edge survives) versus a card that cannot move boards at all today (`multi-board-identity` = A) — do not build cross-board handling for a case the model forbids.
- Endpoint deleted; endpoint's board archived; both endpoints deleted in the same batch.
- Import with an edge whose endpoint is absent — report, never silently drop.
- A proposal that creates card X and then links X to Y in the same operation sequence.
- Mutual `duplicates` (A dup B and B dup A) and a `duplicates` chain — contradictory unless a rule forbids them.

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| C# candidate | `docs/analysis/2026-08-30-acceleration-bundle/candidates/dotnet/WorkRelationRules.cs` | The canonicalization idea: `depends-on(A,B)` → `blocks(B,A)`, `relates-to` ordered by endpoint comparison, dependency cycle by reachability | Duplicate detection is a linear scan of an in-memory edge list — no unique index, so it cannot survive a race the issue's own acceptance demands. No access check, no archived-endpoint check, snake_case codes, `OwnerUserId` on the endpoint record does not exist on `Card`. `duplicates`/`spawned-from` get neither canonical ordering nor a cycle rule |
| C# candidate tests | `.../candidates/dotnet/tests/WorkRelationRulesTests.cs` | Case names | Candidate namespace only |
| Diagram | `.../diagrams/work-model.svg` | Shows links as a separate model from hierarchy | Lists only four relation types — `depends-on` is missing |
| Blueprint | `.../architecture/WORK_MODEL_IMPLEMENTATION_BLUEPRINT.md` §2 (`WorkItemLink`), §5, §8 | Table shape and relation rules | See its validation preface |

## Corrections to the bundle

1. **Bundle pack:** "Depends on: #2087". **True:** nothing in this issue reads `WorkItemType` or
   `ParentCardId`; ADR-0060 keeps parent as a dedicated column and relations as a separate table
   (line 40–41). **Consequence:** the real coupling is the shared-file freeze
   (`Card` DTOs, proposal registry, EF snapshot, export schema), not a functional prerequisite —
   so `WM-LINK-01` can run concurrently with `WM-TYPE-01` under one integration owner.
2. **Bundle:** error codes `work_link_self`, `work_link_duplicate`, `work_link_scope_mismatch`,
   `work_dependency_cycle`. **True:** `ErrorCodes` (`Domain/Exceptions/DomainException.cs`) is a
   closed 15-member PascalCase set and `ResultExtensions.ToHttpStatusCode` maps anything unknown to
   **500**. **Consequence:** these must map onto `ValidationError` (400) or `Conflict` (409), or
   `ErrorCodes` must be extended with its HTTP mapping in the same PR.
3. **Bundle:** the candidate is presented as the uniqueness guard, and the required evidence asks for
   "uniqueness under race". **True:** `WorkRelationRules.ValidateAndCanonicalize` scans a supplied
   `IReadOnlyCollection<CandidateWorkEdge>`; two concurrent requests both see a clean list.
   **Consequence:** the guard is a database unique index over the canonical column tuple; the
   candidate supplies only the canonicalization rule that must run before every insert.
4. **Bundle:** "Deleting an endpoint removes or tombstones the relation according to export/audit
   policy". **True:** no policy exists anywhere in the repository, and the FK is created the moment
   the table is. **Consequence:** this is a decision `WM-LINK-01` must record, not a later cleanup.
5. **Bundle §5:** "Same board and **access-checked** endpoints". **True:** the candidate performs no
   access check at all — it compares `OwnerUserId` on a record type that has no counterpart on
   `Card`. **Consequence:** authorization goes through `IAuthorizationService`'s owner-or-access
   predicates; a `BoardAccess` row alone is insufficient because a board owner deliberately holds none.
6. **Bundle:** the archived-endpoint edge case is listed but unspecified. **True:** ADR-0063 already
   fixes the answer at the board level — any card write on an archived board is `InvalidOperation`
   → `409`. **Consequence:** inherit that contract rather than inventing a link-specific rule;
   there is still no *card*-level archive state (`#2185`).
7. **Bundle §8 export order** puts "link edges" at step 5 with deterministic ID remapping. **True:**
   `ImportCardDto` has no id and matches its column by name. **Consequence:** edges have no import
   anchor; adding a card key to the board JSON contract is a prerequisite for the export slice and
   is a shared-contract change.
8. **Bundle:** `relates-to` canonical order via endpoint comparison. **True in .NET:**
   `Guid.CompareTo` orders by internal fields, which is deterministic but does **not** match SQLite's
   ordering of the stored value. **Consequence:** the canonical order must always be computed in C#
   before insert; a database-side check constraint written against SQL ordering would disagree.
9. **Bundle:** the diagram is offered as the issue's evidence image. **True:** `work-model.svg` lists
   `relates · blocks · duplicates · spawned-from` — four of the five admitted types.
   **Consequence:** do not paste it into the issue as the contract picture without correcting it.
