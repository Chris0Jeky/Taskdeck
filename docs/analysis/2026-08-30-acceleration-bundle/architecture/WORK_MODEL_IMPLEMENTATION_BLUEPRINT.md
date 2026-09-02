# Work-model implementation blueprint

> **Validated 2026-09-02 against `main` `de488fea0`.**
> - **Nothing in this blueprint is shipped.** `grep` across `backend/src` and `frontend/taskdeck-web/src` returns **0 files** for `WorkItemType`, `ParentCardId`, `CardAssignment`, `AssigneeId`, `WorkItemLink` and `CustomFieldDefinition`. `Card` carries exactly `BoardId, ColumnId, Title, Description, DueDate, IsBlocked, BlockReason, Position, CardLabels`. Read §1's tree as a target, not a description.
> - **§3's operation vocabulary does not match the shipped dispatcher.** `OperationHandlerRegistry` dispatches on a `(targetType, actionType)` string pair — `targetType` is one of `card` / `board` / `column`, `actionType` one of `create` / `update` / `move` / `archive` plus the label verbs. There is no `work-item.*` namespace. Worse, `ProposalOperationInputValidator` accepts **any** identifier-like token at create time (regex `\A[A-Za-z][A-Za-z0-9_.-]*\z`, deliberately not an allowlist), so an unrecognised action is stored and previewed happily and fails only at Apply. A new operation must land in `ProposalOperationContractValidator` (the `#1319` preview==apply seam) and the registry in the same PR.
> - **Every snake_case error code in this blueprint's companion material is unusable as written.** `ErrorCodes` (`backend/src/Taskdeck.Domain/Exceptions/DomainException.cs`) is a closed 15-member PascalCase set and `ResultExtensions.ToHttpStatusCode` maps anything unknown to **500**. Map onto `ValidationError` (400), `Forbidden` (403), `Conflict` / `InvalidOperation` (409), or extend the set with its HTTP mapping deliberately.
> - **§2's "concurrency token/version participates in reparent commands" has no card-level counterpart.** Cards have no row version. The shipped mechanisms are `UpdateCardDto.ExpectedUpdatedAt` and the ADR-0063 / `#2114` non-advancing board marker (`Board.RecordCardMutation()` joined to a conditional `Board.ConcurrencyToken` update). Reuse those two.
> - **§2's owner comparison is unsafe.** `Card` has no owner field and `Board.OwnerId` is `Guid?`; two nulls compare equal. Scope on `BoardId`, which is non-null. Participation and permission go through `IAuthorizationService`'s owner-or-access predicates, never a `BoardAccess` row alone — a board owner deliberately holds none, and `BoardAccessService.GetBoardAccessListAsync` therefore omits the owner from any participant list built on it.
> - **§8's export order assumes an ID remapping that does not exist.** `ExportBoardDto` serialises `CardDto` (which has an `Id`), but `ImportBoardDto` uses `ImportCardDto(Title, Description, ColumnName, Position, DueDate, Labels)` — **no id**, column resolved by name — and the importer runs `new Card(board.Id, column.Id, …)`. ADR-0060 records the same fact. Parent references, link edges, assignments and field values therefore have no import anchor; adding a card-level key to the board JSON contract is a prerequisite for every round-trip claim in §8.
> - **§4's archived-parent rules have no substrate.** `Card` has no archive state. The archive-card proposal operation maps onto `Block` (commit `c7f865674`, PR `#2216`, using `OperationHandlerRegistry.ArchiveCardBlockReason`) — no longer the silent no-op `#2185` describes, but still not an archive. ADR-0060 makes a real card-archive state a prerequisite for the parent-lifecycle archive half; delete-side detach is not blocked.
> - **§6's estimate unit and §4's depth convention are presented as settled and are not.** ADR-0062 distinguishes "estimated effort" from "story points or relative size" and ratifies neither as *the* built-in estimate; ADR-0060 says "a hard depth cap of 3" without saying whether 3 counts nodes or edges (the bundle candidate silently counts nodes). Both must be recorded on their issues before code.
> - **§7's custom-field rules are correct but deferred.** ADR-0062 `custom-field-timing` = B defers the generic foundation until after ADR-0061 stage 2 ("Dependable small-team alpha"), and ADR-0061 itself is "Accepted as direction only, evidence pending" with Stage 1 gated on `#1772`. Note that §2's "do not store arbitrary object JSON" rule is contradicted by the bundle's own `CustomFieldValueValidator.cs`, which validates a `JsonElement`.
>
> The body below is the bundle text, unedited.

## 1. Canonical model

Keep `Card` as the compatible work-item identity for v0.4. Extend it narrowly rather than introducing a parallel canonical item.

```text
Card
 ├─ WorkItemType: Task | Epic | Spike
 ├─ ParentCardId? (same board, depth ≤3, acyclic)
 ├─ Assignments[]
 ├─ EstimateMinutes?
 ├─ WorkItemLinks[]
 └─ CustomFieldValues[]
```

Board/column remain placement and access context. Assignment is not authorization. Parent is not a typed link.

## 2. Tables

### Card extension

- `WorkItemType` non-null default `Task`.
- `ParentCardId` nullable FK to Card.
- Index `(BoardId, ParentCardId, Position)` or existing ordering-compatible equivalent.
- Concurrency token/version participates in reparent commands.

### WorkItemAssignment

- `CardId`, `PrincipalId`, `AssignedAt`, `AssignedByPrincipalId`.
- Unique `(CardId, PrincipalId)`.
- Target must be an eligible participant with board access at command time.
- Removing board access does not silently delete history; current assignment becomes inactive/unassignable according to an explicit rule.

### WorkItemLink

- `Id`, `BoardId`, `FromCardId`, `ToCardId`, `RelationType`, creator/created-at.
- Parent excluded.
- `relates-to` stored with canonical endpoint order.
- `blocks` and `depends-on` should be one canonical directed dependency with inverse display labels, preventing duplicate inverse rows.
- `duplicates`: duplicate → canonical.
- `spawned-from`: child/work result → source.

### CustomFieldDefinition

- board-scoped ID, stable key/name, type, constraints, option set/version, retired-at.
- owner/Admin manages definitions; owner/write access edits values.
- rename preserves ID and values.

### CustomFieldValue

Prefer typed nullable columns or one canonical serialized scalar plus a type-specific normalized/indexed column where queries require it. Do not store arbitrary object JSON.

Candidate columns:

- `TextValue`
- `DecimalValue` or invariant decimal string
- `DateValue` (date-only normalized string or provider-supported date mapping)
- `BooleanValue`
- `OptionId`
- `UrlValue`

A DB/domain XOR check should ensure exactly the column matching the definition type is populated.

## 3. Command architecture

All mutations follow:

```text
request DTO
  → actor/board access
  → current aggregate + related records
  → shared pure validator
  → optimistic concurrency check
  → mutation + audit
  → commit
  → realtime invalidation
```

Proposal preview invokes the same validator over a projected state. Apply reruns validation against current state and produces a conflict instead of trusting stale preview.

Suggested operation vocabulary:

- `work-item.set-type`
- `work-item.set-parent`
- `work-item.detach-parent`
- `work-item.archive-subtree` (explicit confirmation and preview only)
- `work-item.add-link` / `remove-link`
- `work-item.assign` / `unassign`
- `work-item.set-estimate`
- `work-item.set-custom-field` / `clear-custom-field`

## 4. Hierarchy rules

- Same owner and board.
- No self-parent.
- Maximum depth 3 including the item according to one documented convention; tests must spell out levels.
- Traverse ancestors with a visited set so corrupted existing data fails closed.
- Reparent validates the whole resulting descendant subtree does not exceed depth, not only the moved node.
- Parent archive/delete default: detach direct children with auditable events.
- Cascade archive is a distinct confirmed operation whose preview lists every affected ID/count.
- Archived parent cannot receive a new child.

## 5. Relation rules

- Same board and access-checked endpoints.
- No self-link.
- Stable uniqueness under concurrency.
- Deleting an endpoint removes or tombstones the relation according to export/audit policy; never leaves an unsafe dangling FK.
- Relation does not confer read access.
- Dependency cycle policy should be explicit. Recommended: reject blocks/depends-on cycles with a bounded graph traversal and stable conflict code.

## 6. Assignments and estimates

- Assignment targets an eligible human participant by default.
- Multiple assignees are a set; duplicates are idempotent.
- Estimate is nullable integer minutes, non-negative, bounded to a sensible maximum.
- `null` = not estimated; `0` = explicitly zero.
- Roll-ups are current-state read projections, labelled “assigned estimate,” not capacity, time spent or historical activity.
- For multiple assignees, choose and document whether each assignee sees the full estimate (recommended for responsibility view) or a divided estimate. Do not silently imply allocation.

## 7. Custom-field rules

- Definition scope must match the card board.
- Type is immutable once values exist unless a dedicated migration exists.
- Retirement prevents new edits by default but preserves reads/export.
- Removing a single-select option with values should retire the option, not delete it.
- Dates are date-only; locale affects display, never storage.
- Numbers reject NaN/infinity and enforce precision/range.
- URLs permit only explicit schemes; normalize for display without silently changing semantic value.

## 8. Export/import order

1. boards/columns and participants/access;
2. cards without parents or links;
3. parent references;
4. assignments and estimates;
5. link edges;
6. field definitions/options;
7. field values;
8. audit/provenance where supported.

Import stages unresolved references and reports them; it does not silently drop them. ID remapping must be deterministic and recorded.

## 9. UI architecture

- Keep the card surface compact; advanced fields use progressive disclosure.
- Parent picker searches only valid same-board candidates and explains why invalid candidates are disabled.
- Relationship editor shows human inverse labels.
- Assignment picker distinguishes board access from assigned state.
- Estimate editor displays units but sends canonical minutes.
- Field editors are generated from definition type, with accessible labels/errors.
- Tree/roll-up views are read models; do not make the base board depend on an expensive recursive load.

## 10. PR sequencing

1. Shared schema/DTO/error/export contract PR.
2. Hierarchy domain/service/API and migration.
3. Typed links in new files/table.
4. Assignment result from M4 #2240, then estimate/roll-up extension.
5. Custom-field definition/value subsystem.
6. Separate UI verticals.
7. MCP/proposal/export integration and E2E convergence.

One integration owner serializes `Card.cs`, DbContext/model snapshot, central DTOs, proposal registry and export schema.
