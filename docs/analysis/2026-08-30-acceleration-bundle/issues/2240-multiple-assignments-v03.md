# WM-ASSIGN — Multiple assignments per card, the v0.3 sub-slice (#2240)

Last Updated: 2026-09-02

> Curated from the v0.3/v0.4 acceleration bundle (grounded `221aa88c8`, 2026-08-30) against `main` `de488fea0` on 2026-09-02 under tracker #2376 (follow-up to #2348). Planning input, not authority: the live issue, the accepted ADRs and `docs/STATUS.md` win. Corrections to the bundle are listed in the last section.

## Outcome

Give a card a set of assignees drawn from the board's authorized participants, through the direct
human API and through proposal preview/apply, granting no access and adding no estimate, roll-up or
participant directory. **This is the ADR-0060 stage-3 Assignment foundation built from scratch, not
the widening of an existing field** — and that is precisely why the issue currently carries an
unresolved design fork.

## Live dependencies (verified 2026-09-02)

| Issue / artefact | State | What it must supply first | Blocks |
| --- | --- | --- | --- |
| **The design fork on `#2240`** (maintainer comment, 2026-08-30) | **unresolved** | Option **A** — admit it as the stage-3 Assignment foundation in v0.3 (L, not S) — or option **B** — defer to v0.4 with `#2093` | **everything in this issue**. It is not tracked in `OUTSTANDING_TASKS.md`, so it can be lost between sessions |
| ADR-0060 `participant-substrate` = A | **Accepted** | Participation = board ownership **OR** a board-access row, evaluated through the authorization service, never a `BoardAccess` row alone | the eligibility check |
| ADR-0063 archived-board write protection | **Accepted** | The `409` contract this issue's own scope cites | the concurrency tests |
| `#2093` participants / estimates / roll-ups | **open**, v0.4 | Nothing. `#2093` **consumes** this schema | — |
| `#2186` item 5 (assignment-vs-revocation criterion) | **closed**; the criterion lives as a comment on `#2093` (2026-08-29) | The detach-or-retain rule for revoked access | the revoke path |

Verified absent on `main`: `grep AssigneeId` → **0 files**; `grep CardAssignment` → **0 files**
across `backend/src` and `frontend/taskdeck-web/src`. `Card.cs` carries exactly `BoardId`,
`ColumnId`, `Title`, `Description`, `DueDate`, `IsBlocked`, `BlockReason`, `Position`, `CardLabels`.
The only "assignee" anywhere is the transient free-text `AssigneeHint` in
`Application/DTOs/CaptureTriageContracts.cs` (LLM triage, bounded to 100 chars, never a user id) and
a display-only unbound `assignee?: string | null` prop in
`frontend/taskdeck-web/src/views/paper/PaperCardDetailView.vue`.

## Child slices (one PR each, in order)

Ordering assumes the fork is ruled **A**. Under **B** this issue closes into `#2093` and none of the
below is startable in v0.3.

| Id | Outcome | Depends on | Mode | Startable before predecessors merge? |
| --- | --- | --- | --- | --- |
| `WM-ASSIGN-01-contract` | `CardAssignment` entity (`CardId`, `UserId`, `AssignedAt`, `AssignedByUserId`), unique `(CardId, UserId)` index, EF configuration, additive migration + down path, eligibility validator over `IAuthorizationService` | fork ruled A | contract-only | **No — blocked by the ruling.** The moment A is recorded this becomes the startable slice; nothing else gates it |
| `WM-ASSIGN-02-commands` | Assign / unassign through the card service, idempotent set semantics, audit row, `boardMutation` event | 01 | implementation | No |
| `WM-ASSIGN-03-proposal` | The assignee-set proposal operation with preview == apply parity | 02 | implementation | No |
| `WM-ASSIGN-04-participants-read` | The owner-or-access participant list the picker needs — `GetBoardAccessListAsync` returns access rows only and therefore **omits the owner** | 01 | implementation | No. Flag it: this is close to the "participant directory" the issue excludes |
| `WM-ASSIGN-05-revocation` | The detach-or-retain rule when board access is revoked, audited and delivered over realtime | 02 | implementation | No |
| `WM-ASSIGN-06-export-deletion` | Export/import round-trip and the account-deletion behavior | 02 | implementation | No |
| `WM-ASSIGN-07-ui` | Paper card + inspector assignee set; Legacy skin unaffected | 03 | implementation | No |

## Architecture

| Concern | Type | Status | Note |
| --- | --- | --- | --- |
| Principal | `User` entity, referenced by `Guid` throughout | **exists** | ADR-0060: `User` is the v0.3 principal; a later rename to Participant is a table rename |
| Participation predicate | `IAuthorizationService.CanWriteBoardAsync` / `GetWritableBoardIdsAsync` — "exactly the one `BoardAccess.CanWrite()` admits (Owner / Admin / Editor) **plus board ownership**" | **exists** | The correct eligibility check. It already solves the solo-owner case the ADR warns about |
| Participant **listing** | `BoardAccessService.GetBoardAccessListAsync(boardId)` → `BoardAccesses.GetByBoardIdAsync` mapped to `BoardAccessDto` | **exists, insufficient** | Returns access rows only. A board owner holds no access row, so the owner is missing from the list a picker would render |
| Access revocation | `BoardAccessService.RevokeAccessAsync` — `BoardAccesses.DeleteAsync(access)` then save | **exists** | A **hard delete**. No tombstone, no audit call in that method, no realtime event. Nothing today would notice an assignment pointing at the revoked user |
| Account deletion | `AccountDeletionService.DeleteAccountAsync` — step 7 deletes every `BoardAccess` row; step 8 **anonymizes and deactivates the `User` row rather than deleting it** | **exists** | Decisive for the join-table shape: a `CardAssignment.UserId` FK to `User` will **not** dangle on account deletion. Board access, however, does vanish — so deletion and revocation need the same rule |
| Optimistic concurrency | `Board.ConcurrencyToken` + `Board.RecordCardMutation()`; `UpdateCardDto.ExpectedUpdatedAt` | **exists** | Cards have no row version. Assignment writes are card writes and must join the ADR-0063 conditional board update |
| Realtime | `BoardRealtimeEvent(BoardId, entityType, action, entityId, timestamp)` on `boardMutation`, group `board:{id:N}` | **exists** | Reuse with `entityType: "card"` (or `"assignment"`); do not add a channel |
| Proposal dispatch | `OperationHandlerRegistry.ExecuteCardOperationAsync` — `create` / `update` / `move` / `archive` + label verbs | **exists** | The assignee-set operation is a new verb in this switch **and** in `ProposalOperationContractValidator` |
| Preview == Apply | `ProposalOperationContractValidator.ValidateAsync` (`#1319`) | **exists** | Apply re-validates against current state; an assignee whose access was revoked between approve and execute must fail there, not be silently dropped |
| Board JSON import | `ImportCardDto` — no card id, no user reference | **exists** | Assignments cannot round-trip through board JSON without a card key *and* a user-identity decision (exporting user ids across instances is a privacy question, not a serialization one) |
| `CardAssignment` | — | **new** | Join table. Its shape decides export, deletion anonymization and audit, which is exactly why the fork calls it L rather than S |

## Implementation plan

**Preflight.** The fork must be ruled first. Record the ruling on `#2240` and add it to
`OUTSTANDING_TASKS.md` — it is a maintainer decision that currently exists only inside an issue
comment. Then re-read ADR-0060 `participant-substrate` and the `#2093` revocation criterion comment.

**Sequence (option A).** 01 → 02 → 03, then 04, 05, 06, 07.

**Producer-owned paths (all new):** `backend/src/Taskdeck.Domain/Entities/CardAssignment.cs`,
`backend/src/Taskdeck.Infrastructure/Persistence/Configurations/CardAssignmentConfiguration.cs`,
`backend/tests/Taskdeck.Domain.Tests/WorkModel/`, `backend/tests/Taskdeck.Application.Tests/WorkModel/`.

**Integration-owner seams:** `Application/DTOs/CardDto.cs`, `Application/Services/CardService.cs`,
`Application/Services/BoardAccessService.cs` (revocation), `Application/Services/AccountDeletionService.cs`,
`Application/Services/Pipeline/OperationHandlerRegistry.cs`,
`Application/Services/Pipeline/ProposalOperationContractValidator.cs`,
`Infrastructure/Persistence/TaskdeckDbContext.cs`,
`Infrastructure/Migrations/TaskdeckDbContextModelSnapshot.cs`,
`frontend/taskdeck-web/src/types/board.ts`.

**Rollout / rollback.** Additive empty table; reads before writes; tested down migration retained
until a release containing assignment writes ships. There is no legacy single-assignee value to
back-fill — the migration has no data step at all, which is the one way this slice is *smaller* than
the issue text implies.

**Definition of done.** ADR-0060's full cross-cutting clause list, plus the two things this issue
uniquely owns: the revocation rule and the extension seam note for `#2093`.

## Test plan

- [ ] Domain: a card holds a set; assigning the same user twice is idempotent, not an error or a second row — `dotnet test backend/tests/Taskdeck.Domain.Tests/Taskdeck.Domain.Tests.csproj -c Release -m:1 --filter "FullyQualifiedName~Assignment"`
- [ ] Application: **a board owner with no `BoardAccess` row can be assigned** — the exact case ADR-0060 flags and the one a row-only check breaks
- [ ] Application: a user with neither ownership nor access cannot be assigned; the failure is a stable code, not a 500
- [ ] Application: assigning grants no access — the assignee's `CanReadBoardAsync` result is unchanged before and after
- [ ] Persistence: the unique `(CardId, UserId)` index holds under a two-writer race — `dotnet test backend/tests/Taskdeck.Integration.Tests/Taskdeck.Integration.Tests.csproj -c Release -m:1`
- [ ] Application: preview and apply of an assignee-set change agree; an assignee revoked between approve and execute makes Apply fail rather than silently drop
- [ ] Application: `RevokeAccessAsync` resolves existing assignments by the recorded rule, emits audit **and** a `boardMutation` event so an open board stops rendering the stale assignee
- [ ] Application: `DeleteAccountAsync` leaves no assignment pointing at a deleted row and reports a count in `AccountDeletionResultDto`
- [ ] Integration: an assignment write racing a board archive returns `409` (ADR-0063)
- [ ] Integration: migration from empty and from a populated database; down migration exercised — `--filter "FullyQualifiedName~MigrationBootstrap"`
- [ ] Api: the assignee set is present in the card read contract and absent-safe for old clients — `dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release -m:1`
- [ ] Frontend (mutation-checked): inspector control and review diff rendering — `cd frontend/taskdeck-web; npx vitest --run --maxWorkers=2 <spec>`
- [ ] Docs: `docs/STATUS.md` line; `node scripts/check-docs-governance.mjs`

## Edge cases

- Board owner with no access row — must be assignable (the ADR's named failure mode).
- Assigning a user whose access is revoked in the same second; revoking access for a user with live assignments.
- Account deletion of an assignee: the `User` row survives anonymized, so the FK holds but the displayed name changes — decide what the board shows.
- Two concurrent assigns of the same user; concurrent assign and unassign.
- An assignment write while the board is being archived (`409`).
- A proposal approved with assignee X, executed after X lost access.
- Export to another instance where the user ids mean nothing.
- Empty assignee set versus no assignment — one state, not two.
- The Paper skin renders the set; the Legacy skin, which never had the field, must keep working untouched.

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| Issue pack | Bundle `06_MILESTONE_4_AUDIT/issue-packs/2240.md` (**not** archived in this repository — the archive carries only `audit-m4/{INDEX,README,HIGH_LEVERAGE_RESIDUALS,TRACKER_DRIFT}.md`) | The "single assignment-contract owner" framing and the extension-note-for-`#2093` idea | Calls it a "narrow v0.3 assignment precursor"; the live comment shows it is the whole stage-3 foundation |
| Blueprint | `.../architecture/WORK_MODEL_IMPLEMENTATION_BLUEPRINT.md` §2 (`WorkItemAssignment`), §6 | Field list and the set-semantics rule | Uses `PrincipalId`; the v0.3 principal is `User`. See its validation preface |
| Diagram | `.../diagrams/work-model.svg` | "Assignment — CardId + PrincipalId — does not grant access" | Explanatory only |
| Collision matrix | `.../executive/COLLISION_MATRIX.md` | Names `Card` / Card DTOs / proposal operations / export as one exclusive contract lane for `#2087`/`#2092`/`#2093`/`#2094` | **Omits `#2240`** from that row even though it is the first writer into it |

## Corrections to the bundle

1. **Bundle pack:** "This is the narrow v0.3 assignment precursor … it should own the assignment
   schema/command contract." **True:** correct about ownership, wrong about "narrow". There is no
   existing assignee to widen (`grep AssigneeId` → 0 hits), so the slice is the ADR-0060 stage-3
   Assignment foundation plus a participant read surface. **Consequence:** the size estimate the
   bundle implies is wrong; the live comment's option A already says "L, not S".
2. **Bundle pack residual:** "Freeze assignment identity, uniqueness and access semantics" listed as
   startable work. **True:** the issue carries an **unresolved maintainer design fork** posted
   2026-08-30 and nothing may start until it is ruled. **Consequence:** the pack's
   `active-shared-contract` state is wrong; the state is *blocked on a ruling*.
3. **Bundle:** "#2093 should not touch the same schema until this merges." **True and worth keeping**
   — but it is stated as if `#2240` were in flight. **True on `main`:** no assignment code exists.
   **Consequence:** the sentence is a future constraint, not a current one.
4. **Bundle test minimum:** "non-member target" as the eligibility case. **True:** the case that
   actually breaks a naive implementation is the **board owner**, who deliberately holds no
   `BoardAccess` row. **Consequence:** add the owner case; a "non-member" test alone passes on a
   broken row-only check.
5. **Bundle test minimum:** "export/import". **True:** `ImportCardDto` has neither a card id nor any
   user reference, and the board JSON importer mints fresh cards. **Consequence:** either the board
   JSON contract gains a card key and a user-identity policy, or the slice records an explicit
   "assignments do not round-trip through board JSON" limitation.
6. **Bundle:** silent on account deletion. **True:** `AccountDeletionService` anonymizes and keeps
   the `User` row (step 8) while hard-deleting every `BoardAccess` row (step 7).
   **Consequence:** deletion does not dangle the assignment FK, but it does remove the participation
   evidence — the same rule chosen for revocation must cover it, and
   `AccountDeletionResultDto` needs a new counter.
7. **Bundle `COLLISION_MATRIX.md`:** the `Card` / DTO / proposal / export exclusive lane lists
   `#2087`, `#2092`, `#2093`, `#2094`. **True:** `#2240` is the first issue that would write into
   that lane. **Consequence:** add it, or the freeze fails on its first user.
8. **Bundle blueprint §2:** "Removing board access does not silently delete history; current
   assignment becomes inactive/unassignable according to an explicit rule." **True:** the explicit
   rule is already specified on `#2093` (comment 2026-08-29, from `#2186` item 5) as a binary choice —
   **detach** (removed, audited, delivered over realtime, listed in preview/apply if reachable by
   proposal) or **retain a durable former-participant record**. **Consequence:** this issue must pick
   one and state it; the blueprint's "according to an explicit rule" is not itself a rule.
