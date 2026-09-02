# WM-PART — Participants, multiple assignments, estimates and roll-ups (#2093)

Last Updated: 2026-09-02

> Curated from the v0.3/v0.4 acceleration bundle (grounded `221aa88c8`, 2026-08-30) against `main` `de488fea0` on 2026-09-02 under tracker #2376 (follow-up to #2348). Planning input, not authority: the live issue, the accepted ADRs and `docs/STATUS.md` win. Corrections to the bundle are listed in the last section.

## Outcome

Extend the assignment substrate `#2240` owns with one built-in estimate and current-state roll-ups
that are derived on read, never stored and never client-written — and label them honestly as
assignment/estimate totals, not capacity, time spent or historical activity.

## Live dependencies (verified 2026-09-02)

| Issue / artefact | State | What it must supply first | Blocks |
| --- | --- | --- | --- |
| `#2240` multiple assignments | **open**, v0.3, carrying an **unresolved maintainer design fork** (comment 2026-08-30) | The `CardAssignment` substrate this issue extends. If the fork is ruled **B**, `#2240` folds back into this issue and the substrate becomes this issue's own first slice | the participant / assignment / per-participant roll-up half |
| ADR-0060 `participant-substrate` = A | **Accepted** | Participation = board ownership **OR** a board-access row, checked through the authorization service — never a `BoardAccess` row alone | the eligibility rule |
| ADR-0062 `adr0062-gate-on-2093` = A | **Accepted** (ratified 2026-08-29, recorded on `#2091`, now **closed**) | The built-in-measure boundary and the derived-aggregate rule | the estimate + roll-up half — **the gate is satisfied** |
| The revocation criterion (`#2093` comment 2026-08-29, from `#2186` item 5) | **recorded**; `#2186` closed | Detach or retain-a-durable-record when board access is revoked | whichever of `#2240`/`#2093` builds assignments |

Verified absent on `main`: no `CardAssignment`, no estimate field, no roll-up read model,
no Participant entity. `Card.cs` has nine members and none of them is any of these.

## Child slices (one PR each, in order)

| Id | Outcome | Depends on | Mode | Startable before predecessors merge? |
| --- | --- | --- | --- | --- |
| `WM-EST-01-estimate` | Nullable non-negative bounded estimate on the card path, domain validation, additive migration + down path, `CardDto` + `types/board.ts` field, audit of old/new | shared contract freeze only | contract-only | **Yes — this is the startable-now slice.** The estimate has **no assignment dependency**; it is one nullable scalar on `Card` and ADR-0062's gate is already satisfied |
| `WM-EST-02-commands` | Set/clear estimate through the human API and through a proposal operation with preview == apply parity | 01 | implementation | No |
| `WM-PART-01-reconcile` | Adopt the merged `#2240` assignment contract; publish the extension note | `#2240` | implementation | No — and today `#2240` is itself blocked on a ruling |
| `WM-PART-02-participants` | The owner-or-access participant read surface — `GetBoardAccessListAsync` omits the owner | `#2240` | implementation | No |
| `WM-ROLL-01-readmodel` | Board/column and per-participant roll-ups derived on read, no stored aggregate, explicitly labelled | `WM-EST-01`, `#2240` | implementation | No |
| `WM-ROLL-02-ui-export` | Assignment picker, estimate editor with unit display, roll-up labels, export round-trip | above | implementation | No |

## Architecture

| Concern | Type | Status | Note |
| --- | --- | --- | --- |
| Principal | `User` (`Guid` id) | **exists** | ADR-0060: the v0.3 principal. No Participant table until `#1772` evidence |
| Participation predicate | `IAuthorizationService.CanWriteBoardAsync` / `GetWritableBoardIdsAsync` — the `BoardAccess.CanWrite()` set (Owner / Admin / Editor) **plus board ownership** | **exists** | The owner-or-access check both ADRs demand |
| Participant listing | `BoardAccessService.GetBoardAccessListAsync` → access rows only | **exists, insufficient** | Omits the board owner. A picker built on it hides the solo user from their own board |
| Access revocation | `BoardAccessService.RevokeAccessAsync` — hard `DeleteAsync` on the `BoardAccess` row, no tombstone, no audit call in that method, no realtime event | **exists** | The exact hazard the 2026-08-29 acceptance criterion was written for |
| Account deletion | `AccountDeletionService` — deletes every `BoardAccess` row (step 7), **anonymizes and keeps** the `User` row (step 8), returns counts in `AccountDeletionResultDto` | **exists** | A `UserId` FK survives deletion; participation evidence does not |
| Audit | `AuditLogDto(Id, EntityType, EntityId, Action, UserId, UserName, Changes, Timestamp)`; `CardService.BuildCardChangeSummary` composes the `Changes` string | **exists** | Estimate old/new fits the existing summary pattern; extend that method rather than inventing a second shape |
| Optimistic concurrency | `UpdateCardDto.ExpectedUpdatedAt`; `Board.ConcurrencyToken` + `Board.RecordCardMutation()` (ADR-0063 / `#2114`) | **exists** | No card row version |
| Realtime | `BoardRealtimeEvent(BoardId, entityType, action, entityId, timestamp)` on `boardMutation` | **exists** | A roll-up is derived, so it needs no event of its own — the card event already invalidates it |
| Proposal dispatch / preview parity | `OperationHandlerRegistry.ExecuteCardOperationAsync`; `ProposalOperationContractValidator.ValidateAsync` (`#1319`) | **exists** | Estimate can ride the existing `update` verb's parameter set instead of adding a verb — decide explicitly |
| Metrics/read-model precedent | `MetricsExportService` / `IMetricsExportService` | **exists** | An existing derived-read surface to model the roll-up query on rather than a new subsystem |
| `CardAssignment`, estimate column, roll-up query | — | **new** | The estimate is the only one of the three with no predecessor |

**Estimate unit is an open decision, not a settled one.** ADR-0062 distinguishes "estimated
effort: expected work required" from "story points or relative size: comparative estimate, not
elapsed time" and ratifies neither as *the* built-in estimate. The bundle recommends integer minutes.
Record the choice on the issue before `WM-EST-01`; `null` = not estimated and `0` = explicitly zero
must be two distinct, tested states either way.

## Implementation plan

**Preflight.** Read all four `#2093` comments — the `participant-substrate` ruling, the ADR-0062
rulings, the revocation acceptance criterion, and the v0.4 move that carved `#2240` out. Check
whether the `#2240` fork has been ruled; if it was ruled **B**, fold `WM-ASSIGN-01` into this issue
as its first slice.

**Sequence.** `WM-EST-01` → `WM-EST-02` now; the participant and roll-up chain after `#2240`.

**Producer-owned paths:** `backend/src/Taskdeck.Application/WorkModel/RollUp*` (new),
`backend/tests/Taskdeck.Application.Tests/WorkModel/` (new).

**Integration-owner seams:** `Domain/Entities/Card.cs`, `Application/DTOs/CardDto.cs`,
`Application/Services/CardService.cs`, `Application/Services/BoardAccessService.cs`,
`Application/Services/AccountDeletionService.cs`,
`Application/Services/Pipeline/OperationHandlerRegistry.cs`,
`Application/Services/Pipeline/ProposalOperationContractValidator.cs`,
`Infrastructure/Migrations/TaskdeckDbContextModelSnapshot.cs`,
`frontend/taskdeck-web/src/types/board.ts`.

**Rollout / rollback.** The estimate column is additive and null for every existing card, so stage 1
changes no behavior. Roll-ups ship last and read-only; because nothing is persisted, rolling them
back is deleting an endpoint. Retain the tested down migration until a release with estimate writes
is cut.

**Definition of done.** ADR-0060's cross-cutting clause list in full, plus the two things unique to
this issue: the roll-up label text is asserted by a test (not left to UI copy), and no aggregate is
persisted anywhere — assert that no new column or table stores a total.

## Test plan

- [ ] Domain: estimate rejects negative values and values past the recorded bound; `null` and `0` are distinguishable and both round-trip — `dotnet test backend/tests/Taskdeck.Domain.Tests/Taskdeck.Domain.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~Estimate"`
- [ ] Application: estimate change appears in the audit `Changes` summary with old and new values
- [ ] Application: estimate set/clear through preview and apply produce identical results
- [ ] Application: **a board owner with no `BoardAccess` row is an eligible assignee** — the ADR's named failure mode
- [ ] Application: assignment grants no access (`CanReadBoardAsync` unchanged before and after)
- [ ] Application: roll-ups are computed from authoritative rows on every read; no persisted total exists; a stale-cache test is impossible by construction and that is the point
- [ ] Application: multi-assignee arithmetic matches the documented interpretation (full estimate per assignee **or** divided — pick one and test it; do not let the sum imply allocation)
- [ ] Application: roll-ups over archived boards / blocked cards behave as documented
- [ ] Application: `RevokeAccessAsync` resolves assignments by the recorded detach-or-retain rule, with audit and a `boardMutation` event
- [ ] Application: `DeleteAccountAsync` leaves no assignment pointing at a deleted row; the result DTO reports it
- [ ] Persistence: unique `(CardId, UserId)` under a two-writer race; migration from empty and populated; down migration — `dotnet test backend/tests/Taskdeck.Integration.Tests/Taskdeck.Integration.Tests.csproj -c Release -m:1`
- [ ] Api: card read contract carries the estimate and the assignee set, absent-safe for old clients — `dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release -m:1`
- [ ] Frontend: estimate editor sends the canonical unit whatever it displays — `cd frontend/taskdeck-web; npx vitest --run --maxWorkers=2 <spec>`
- [ ] Docs: `docs/STATUS.md`; `node scripts/check-docs-governance.mjs`

## Edge cases

- `null` versus `0` estimate — two states, never collapsed.
- A card with three assignees and one estimate: the per-participant roll-up either triple-counts or divides. Whichever is chosen, the label must say so.
- Roll-up over an archived board, a blocked card, or a card whose assignee lost access.
- Board owner with no access row (assignable); a user with read-only access (decide, and test).
- Revocation and account deletion, which are two different events with the same symptom.
- Concurrent estimate edits; an estimate edit racing a board archive (`409`).
- A proposal that sets an estimate on a card created earlier in the same operation sequence.
- Export to another instance: estimates are portable, user references are not.
- Someone later "optimizing" the roll-up into a stored column — the derived-aggregate rule is ratified; guard it with a test.

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| Issue pack | Bundle `01_MILESTONE_5/issue-packs/2093.md` (**not** archived in this repository — no issue packs were committed) | The "do not build a second assignment substrate" rule and the four open decisions (unit, service accounts, deactivation, archived items in roll-ups) | Its `blocked-by-m4-slice` state is right for the participant half and wrong for the estimate half |
| Blueprint | `.../architecture/WORK_MODEL_IMPLEMENTATION_BLUEPRINT.md` §2 (`WorkItemAssignment`), §6 | Set semantics, `null`-vs-`0`, the "do not silently imply allocation" rule for multi-assignee estimates | Uses `PrincipalId`; recommends minutes as though decided. See its validation preface |
| Diagram | `.../diagrams/work-model.svg` | Assignment and estimate as separate concerns from links and fields | Explanatory only |
| Testing | `.../testing/MASTER_TEST_MATRIX.md`, `.../testing/ADVERSARIAL_CASES.md` | Generic cross-cutting floors | Shared boilerplate across every bundle stream — a floor, not coverage for this issue |

## Corrections to the bundle

1. **Bundle pack:** "Recommended state: `blocked-by-m4-slice`" for the whole issue, with
   `WM-PART reconcile` as child PR 1. **True:** the **estimate** half depends on nothing but the
   shared contract freeze — it is one nullable scalar on `Card`, and ADR-0062's
   `adr0062-gate-on-2093` gate is already satisfied because both ADRs are Accepted.
   **Consequence:** `WM-EST-01` is startable now and should be PR 1; the reconcile slice moves behind it.
2. **Bundle:** "First reconcile the merged result of M4 `#2240`". **True:** `#2240` is not merged, not
   started, and blocked on an unresolved maintainer design fork posted 2026-08-30.
   **Consequence:** the sentence describes a state that does not exist; if the fork is ruled **B**,
   the substrate becomes this issue's own first slice rather than an inheritance.
3. **Bundle:** "add **nullable integer-minute** estimates" — stated as the decision.
   **True:** ADR-0062 lists "estimated effort" and "story points or relative size" as *different*
   measures and ratifies neither as the built-in estimate; minutes is the pack's recommendation.
   **Consequence:** record the unit on the issue first. Minutes is a reasonable default but it is a
   product choice, not a ratified one.
4. **Bundle:** treats the participant list as available. **True:** `GetBoardAccessListAsync` returns
   `BoardAccess` rows and therefore **omits the board owner**, who deliberately holds none.
   **Consequence:** an owner-or-access participant read surface is new work — and, as `#2240`'s
   comment notes, it edges into the "participant directory" the issues exclude. Scope it explicitly.
5. **Bundle blueprint §2:** "Removing board access does not silently delete history; current
   assignment becomes inactive/unassignable according to an explicit rule." **True:** the explicit
   rule already exists as an acceptance criterion on this issue (comment 2026-08-29, carried from
   `#2186` item 5): **detach** — removed, audited, delivered over per-board realtime, and listed in
   proposal preview/apply if reachable that way — **or retain a durable former-participant record**.
   **Consequence:** cite the criterion; the blueprint sentence is not itself a rule.
6. **Bundle:** silent on account deletion for assignments. **True:** `AccountDeletionService`
   anonymizes and **keeps** the `User` row while hard-deleting `BoardAccess` rows.
   **Consequence:** the FK does not dangle, but revocation and deletion need the same rule and
   `AccountDeletionResultDto` needs a counter.
7. **Bundle:** "roll-up read model … no stored aggregate" as a design preference.
   **True:** it is a **ratified** constraint — ADR-0062 `aggregate-rollup-semantics` = A, "derived
   read models or queries over authoritative values and events … not custom-field values that
   clients write directly". **Consequence:** state it as a rule with a test, not as guidance, and
   note the ADR permits a cache **under the same read contract** only if the alpha shows latency pain.
8. **Bundle:** offers `work_assignment_target_ineligible` in its expected-error list. **True:**
   `ErrorCodes` is a closed 15-member PascalCase set and `ResultExtensions.ToHttpStatusCode` maps
   anything unknown to **500**. **Consequence:** map onto `ValidationError` (400) or `Forbidden`
   (403) — the eligibility failure is an authorization outcome, so pick deliberately.
9. **Bundle:** "Can service accounts be assignable (recommend no by default)" listed as an open
   decision. **True:** ADR-0060 fixes the substrate as the shipped `User` with no actor-kind
   distinction, so there is nothing in the model to make assignable or not.
   **Consequence:** the question is vacuous in v0.3; drop it rather than answering it.
