# ADR-0060 / ADR-0061 / ADR-0062 — ratification briefs

Last Updated: 2026-08-29

> **Superseded as a status source — the rulings landed the same day.** This is the pre-ruling brief;
> it is kept as the decision record and its "Proposed" statements describe the state it was drafted
> against, not the state now. All three ADRs were ratified in-session on 2026-08-29: **ADR-0060
> Accepted** (`#2084`), **ADR-0062 Accepted** (`#2091`), **ADR-0061 Accepted as direction only,
> evidence pending** (`#1772`, with three CL-1 values still outstanding). `docs/decisions/INDEX.md`
> and each ADR's own "Decisions recorded (2026-08-29)" section are the canonical statuses.

Prepared under the maintainer's 2026-08-29 walkthrough reply **q-1 = A** ("dedicated ratification session — one decision brief per ADR with per-question options"). Nothing here is ratified: all three ADRs remain **Proposed** until the maintainer records rulings on `#2084`, `#1772`, and `#2091`. Every brief was drafted from the ADR text, its owning issues, and shipped reality on `main` `927236bd0`, then adversarially critiqued by an independent agent. Recommendations are evidence-based defaults, not decisions.

How to use: each ADR has a numbered list of sub-decisions with lettered options. Reply per ADR either "accept the recommended letters" or name the overrides (`<id>=<letter>`). **Letters alone do not complete ADR-0061 / CL-1:** `access-boundary` needs the collaborator named, `budget-alerts-cost-owner` needs the monthly ceiling and alert threshold, and `backup-retention-destination` needs the off-platform retention window — supply them in the reply scope (e.g. `collaborator=<handle> ceiling=<USD/month> alert=<USD> retention=<days>`), or the agent records the letters as decided-pending-values and the deployment-critical inputs stay open. After the rulings are recorded on the owning issue, an agent writes a "Decisions recorded" section into the ADR, flips its status, and updates `docs/decisions/INDEX.md`.

## Contents

- [ADR-0060 — Canonical Work Model and Board Compatibility Path](#adr-0060)
- [ADR-0061 — Trusted Shared Instance and Managed SaaS Boundary](#adr-0061)
- [ADR-0062 — Custom Fields, Aggregates, and Threshold Rules — maintainer decision brief (revised)](#adr-0062)

## ADR-0060

### Canonical Work Model and Board Compatibility Path

**Status line:** docs/decisions/ADR-0060-canonical-work-model-and-compatibility-path.md:3 — "- **Status**: Proposed" (Date 2026-08-26; Deciders "Chris0Jeky (maintainer, decision pending)"). Owner issue #2084 is OPEN on milestone "v0.3 — Open Beta + Accountable Agents"; its non-goals state "Merging a Proposed ADR does not ratify it."

**Context.** Shipped reality at origin/main 927236bd0 is Board -> Column -> Card: Card.cs:11 requires BoardId, :14 requires ColumnId, and MoveToColumn (Card.cs:95-99) changes only ColumnId and position; there is no type, parent, relation, project, participant or estimate (greps for WorkItemType and ParentCardId/ParentId return zero source hits in backend/src and frontend/taskdeck-web/src — only compiled DLLs under bin/ match). Authorization is board-scoped and is owner-OR-access, never access alone: AuthorizationService.cs:105 and :121 short-circuit on board.OwnerId == userId before consulting BoardAccess, and BoardRepository.cs:240 reads `board.OwnerId == userId || board.BoardAccesses.Any(...)`; STATUS.md:42 records why ("a board owner deliberately holds no `BoardAccess` row") and STATUS.md:116 repeats it for BoardDto.CanWrite. Realtime is per-board, and every AUTOMATION and MCP write path is proposal-first (manual UI edits remain direct calls through CardsController/CardService by design) (Propose{Create,Update,Move,Archive}CardExecutor.cs; MCP write tools create_card/move_card/update_card/archive_card at WriteTools.cs:80,184,243,334 each create a PROPOSAL). ADR-0060 already settles the target vocabulary, the hierarchy/typed-relation/type separation (:34-42), the Principal/Participant/Persona/Assignment/WorkLog boundaries (:44-58), no direct agent mutation (:60-61), a five-stage compatibility ladder plus per-stage checklist (:63-80), and four rejected alternatives (:96-109). It leaves six numbered choices to the maintainer (:84-91) with "safer defaults" named at :93. Blocking is NOT uniform: only #2087 is gated on those choices ("Implementation remains blocked until ADR-0060 records the first types and hierarchy boundary", repeated as its acceptance line 1). #2092 depends only on "accepted ADR-0060 semantics" and #2093 only on "accepted ADR-0060 identity terms and existing board-access rules" — both already present in the ADR body — so plain acceptance releases those two. REVIVAL_PLAN.md:61 keeps Board -> Column -> Card authoritative until ratification.

**If ratified unmodified.** Accepting the text as-is commits to: the target vocabulary table (:24-32); the hierarchy / typed-relation / item-type / template / recurrence / ongoing-lifecycle separation (:34-42, including \":39 item type is independent of either relationship\"); the Principal / Participant / Persona / Assignment / WorkLog boundaries and the estimate-vs-actual distinction (:44-58); no direct agent or LLM mutation path (:60-61); the five compatibility stages plus the per-stage checklist of permissions, proposal diff/apply, audit, export/import, account deletion, MCP/API, realtime, concurrency, migration bootstrap and rollback (:63-80); and the rejection of a permanent Board parent, an immediate Workspace->Project->WorkItem rewrite, one generic link field, and a graph database (:96-109). Unblocking is per-issue, not all-or-nothing. Plain acceptance DOES release #2092, whose only dependency line is \"Depends on accepted ADR-0060 semantics\" and whose five admitted edge types are already written at :37-38 and re-enumerated in its own scope. It DOES release #2093, whose dependency line is \"Depends on accepted ADR-0060 identity terms and existing board-access rules\" — those terms are already at :46-58 — though without the participant-substrate ruling the implementing agent chooses whether to add identity tables, and without an explicit owner-or-access rule the natural reading of \"authorized participant\" as a BoardAccess row would break assignment on every solo board (AuthorizationService.cs:105,121; STATUS.md:42). It does NOT release #2087, which states \"Implementation remains blocked until ADR-0060 records the first types and hierarchy boundary\" and repeats it as acceptance line 1. #2094 stays blocked on both #2091/ADR-0062 and \"accepted ADR-0060 scope\". Decisions 1-6 (:86-91) otherwise stay open with only the safer defaults at :93 recorded, and OUTSTANDING_TASKS.md:14 keeps all six in one unratified item. The recommended close: ratify with a \"Decisions recorded\" section that answers all six numbered decisions — 1 project-timing, 2 multi-board-identity, 3 hierarchy-boundaries (in the ADR's own \"cross projects\" wording plus the board-scope reframe), 4 first-item-types, 5 custom-fields-timing, 6 time-tracking-fit — plus the four scoping rulings ADR-0060 does not number but its dependants need: compat-path, participant-substrate, relation-scope, and parent-lifecycle.

**Cross-ADR dependencies.**

- ADR-0062 (#2091, Proposed, docs/decisions/ADR-0062-...md:3) duplicates ADR-0060 decisions 5 and 6 in its "Human decisions required" list (:75-79). The ordering is one-way, not a deadlock: ADR-0062:12 says it "does not describe shipped behavior or authorize implementation before the canonical work-model decision in ADR-0060 is accepted" — an implementation gate, not a ratification gate — while #2091's acceptance keeps ADR-0062 Proposed until the maintainer records answers on that issue. So ADR-0060 can be ratified first, and #2091 can be answered at any time; what cannot happen is field/time-tracking implementation before ADR-0060 is Accepted. The custom-fields-timing and time-tracking-fit sub-decisions above exist so ADR-0060's own decision list closes rather than pointing at a document that authorizes nothing yet.
- ADR-0061 (#1772, Proposed) supplies the collaboration and dogfooding evidence that ADR-0060 Stage 5 requires (:74-76) and that project-timing and participant-substrate both defer to.
- ADR-0056 (Accepted) and ADR-0057 (Accepted as direction only) — ADR-0060:60-61 keeps proposal-first for non-human actors. Any new operation (type, parent, typed relation, assignment, estimate) must land in proposal diff/apply, not a second mutation path; the shipped surface is Propose{Create,Update,Move,Archive}CardExecutor.cs plus MCP create_card/move_card/update_card/archive_card at WriteTools.cs:80,184,243,334. #2004 is the related open decision.
- ADR-0063 (Accepted) — archived boards reject card writes; parent/child links (hierarchy-boundaries) and typed edges (relation-scope) must respect archive state, which is the concrete reason same-board scoping is recommended for both.
- ADR-0058 (Accepted) — due dates are calendar days; the #2093 estimate and roll-up values must not be conflated with due-date arithmetic, consistent with ADR-0060:57-58 keeping estimate, remaining effort, logged work, elapsed cycle time, lead time, size and capacity distinct.
- ADR-0051 (Accepted, :3) Decision 1 makes PR merging agent-executable across all file and dependency classes with no owner click required for a path or package category. That is why compat-path option A must say "ADR amendment only" for Stages 4-5: a per-issue ruling is a materially weaker gate under that lane. It is also why ratifying Stages 4-5 now (compat-path B) would widen autonomous admission onto the ownership migration.

| # | Sub-decision | Recommended |
| --- | --- | --- |
| 1 | `project-timing` — ADR-0060 decision 1 (:86) — when does a durable Project concept arrive relative to Boards? | **A** |
| 2 | `multi-board-identity` — ADR-0060 decision 2 (:87) — may one work item appear on several boards, and what is its identity? | **A** |
| 3 | `hierarchy-boundaries` — ADR-0060 decision 3 (:88) — may parent/child hierarchy cross projects; and, since no Project entity exists, may it cross boards? What depth is allowed? | **A** |
| 4 | `parent-lifecycle` — What happens to children when their parent is deleted or archived? | **A** |
| 5 | `first-item-types` — ADR-0060 decision 4 (:89) — which WorkItemType values ship first, and what do existing cards default to? | **A** |
| 6 | `compat-path` — How far up ADR-0060's five-stage compatibility ladder (:63-76) does this ratification authorize execution? | **A** |
| 7 | `participant-substrate` — For #2093, is Participant a new table, or is the shipped User plus board-authorization pair the participant record? | **A** |
| 8 | `relation-scope` — May a #2092 typed edge connect cards that live on two different boards? | **A** |
| 9 | `custom-fields-timing` — ADR-0060 decision 5 (:90) — do generic custom fields enter v0.3 or later, and is that answer recorded in ADR-0060 or delegated? | **A** |
| 10 | `time-tracking-fit` — ADR-0060 decision 6 (:91) — does actual time tracking fit the product thesis, and where is that recorded? | **A** |

#### ADR-0060.1 `project-timing` — ADR-0060 decision 1 (:86) — when does a durable Project concept arrive relative to Boards?

*Why it matters.* ADR-0060 compatibility Stage 4 (:72-73) must satisfy the full per-stage checklist at :78-80 (permissions, proposal diff/apply, audit, export/import, account deletion, MCP/API, realtime, concurrency, migration bootstrap, rollback). REVIVAL_PLAN.md:48 sets v0.3 RC at 2026-09-04 and final at 2026-09-08 or 2026-09-09; REVIVAL_PLAN.md:47 is the v0.2 row (final 2026-09-01). Choosing now sets whether Board stays the ownership root through v0.3.

*Evidence.*

- docs/decisions/ADR-0060-...md:72-73 (Stage 4), :86 (decision 1), :93 (safer defaults)
- backend/src/Taskdeck.Domain/Entities/Board.cs — Board is the ownership root; no Project or Workspace entity exists in backend/src/Taskdeck.Domain/Entities (WorkspaceMode/WorkspaceOnboarding are UI-mode enums, not storage)
- docs/REVIVAL_PLAN.md:48 (v0.3 RC 2026-09-04 / final 2026-09-08 or 09), :59 (full Workspace/Project/WorkItem migration listed under "Later"), :61 (Board -> Column -> Card authoritative until ratification)
- docs/strategy/PRODUCT_DIRECTION.md:84-85 (P4 one project state, many views), :96-97 (P10 broad vision does not admit broad scope)
- #1321 GEN-07 project dossier is already scoped as a per-board read model

*Options.*

- **A.** Not before v0.3 final. Ratify the vocabulary only; Project stays ADR-0060 compatibility Stage 4 and needs its own admitted issue plus collaboration evidence from #1772. — *Consequence:* Board remains the project proxy (as #1321 already assumes). Zero migration risk before 2026-09-08. Cross-board context waits.
- **B.** Lightweight Project in v0.3: an optional grouping row that boards reference (nullable ProjectId), no card moves, no view semantics. — *Consequence:* Adds a schema stage inside the RC window alongside #2087/#2092/#2093; every checklist item at ADR:78-80 must be proven for a mostly cosmetic grouping.
- **C.** Project becomes the required parent now (Workspace -> Project -> WorkItem rewrite). — *Consequence:* This is the ADR's own rejected alternative (:101-103): simultaneous change of persistence, proposals, exports, MCP, audit, realtime and UI ownership.

*Recommendation:* **A** — Nothing shipped needs Project to deliver v0.3's theme ("Open Beta + Accountable Agents", PRODUCT_DIRECTION.md:105). P4 is served today by board-scoped views and #1321 already models the dossier per board. Stage 4 has no admitted implementation issue, and P10 (:96-97) makes admission depend on product proof rather than category completeness. Record "Project after v0.3, gated on #1772 evidence" so the deferral is a recorded ruling rather than an open item. Note: #2087 does NOT wait on this — its dependency line names only #2084 plus the Accepted ADR, and its non-goals say "No Workspace/Project migration in this slice."

*Reversibility:* Easy — nothing is built; adding a nullable ProjectId to Board later is an additive migration.

*Downstream:* #1772, #1321, #2094

#### ADR-0060.2 `multi-board-identity` — ADR-0060 decision 2 (:87) — may one work item appear on several boards, and what is its identity?

*Why it matters.* Card identity today IS its board placement. Multi-board placement would fork identity away from BoardAccess (the security boundary), per-board SignalR invalidation, and every board-targeted proposal operation.

*Evidence.*

- backend/src/Taskdeck.Domain/Entities/Card.cs:11 (BoardId required), :14 (ColumnId required), :95-99 MoveToColumn changes ColumnId and position only
- backend/src/Taskdeck.Domain/Entities/BoardAccess.cs:13-16 — authorization row is (BoardId, UserId, Role, GrantedBy)
- docs/decisions/ADR-0060-...md:74-76 (Stage 5: "Multi-board placement is not implied by earlier stages"), :87, :93 ("no multi-board placement" is the safer default)
- backend/src/Taskdeck.Application/Services/Tools/ProposeCreateCardExecutor.cs, ProposeUpdateCardExecutor.cs, ProposeMoveCardExecutor.cs, ProposeArchiveCardExecutor.cs — proposal ops are board/column-targeted; the MCP write tools are named create_card, move_card, update_card, archive_card at backend/src/Taskdeck.Api/Mcp/WriteTools.cs:80,184,243,334 (each creates a PROPOSAL; there are no `propose_*`-named MCP tools)
- docs/analysis/2026-08-26-v012-dogfooding-reconciliation.md:63 — "one canonical item on several boards" is listed among post-v0.3 candidates

*Options.*

- **A.** One item = exactly one board and one column through v0.3. Identity stays the Card GUID. Stage 5 (canonical item + placements) needs a separate ruling. — *Consequence:* No authorization, realtime or proposal-op redesign. There is no shared-identity cross-board view in v0.3. Whether a non-identity typed edge may cross boards is a separate ruling (see relation-scope); A neither grants nor forbids it.
- **B.** Same as A, and additionally pre-authorize cross-board typed edges here rather than in relation-scope. — *Consequence:* Collapses two questions into one ruling; the #2092 scope-validation and dangling-edge questions then have no separate record and get decided by the implementing agent.
- **C.** Introduce WorkItem + Placement tables now so a card can appear on N boards. — *Consequence:* Breaks BoardAccess-as-boundary, per-board SignalR invalidation and every board-scoped proposal op before any collaboration evidence exists; contradicts ADR:74-76.

*Recommendation:* **A** — Every shipped boundary — authorization (AuthorizationService.cs:105,121), realtime, proposal ops, and the ADR-0063 archived-board write guard — keys on BoardId. The ADR names no-multi-board as its own safer default (:93) and the reconciliation defers it post-v0.3 (:63). Keep A narrow to identity and decide edge scope separately, so #2092's permission and dangling-edge ACs get their own recorded answer.

*Reversibility:* A is cheap to reverse (an additive placement table later). C is very hard to reverse once placements carry audit history.

*Downstream:* #2092, #1772

#### ADR-0060.3 `hierarchy-boundaries` — ADR-0060 decision 3 (:88) — may parent/child hierarchy cross projects; and, since no Project entity exists, may it cross boards? What depth is allowed?

*Why it matters.* #2087 cannot start until the ADR "records the first types and hierarchy boundary" (its acceptance line 1 and its dependency line). Depth and scope rules drive the server-side cycle check, the deletion rule, and export round-trip. ADR:88 is worded "cross projects", but no Project exists at 927236bd0, so a literal answer is vacuous unless the board-scope question is answered alongside it.

*Evidence.*

- docs/decisions/ADR-0060-...md:36 (one optional parent, adjacency list with cycle checks), :39 ("item type is independent of either relationship"), :88 (decision 3), :93 ("no cross-project hierarchy" is the safer default)
- #2087 scope: "Enforce same-owner/scope rules, cycle prevention, and a documented depth policy"; non-goals: "No multiple parents, graph database, or cross-project hierarchy unless explicitly accepted"
- docs/decisions/ADR-0063 (Accepted) — archived boards reject card writes; a cross-board parent would require archive-state checks on two boards
- backend/src/Taskdeck.Domain/Entities/Card.cs — no parent field; grep for ParentCardId/ParentId in backend/src and frontend/taskdeck-web/src returns zero source hits (17 matches are all compiled DLLs under bin/)

*Options.*

- **A.** Same board only; one optional parent; hard depth cap of 3; server-side cycle check; type-agnostic (any admitted type may parent any admitted type, per ADR:39). Record cross-project hierarchy as "no" — vacuously, while no Project entity exists — to be re-decided if Stage 4 is ever ratified. — *Consequence:* Cheap server-side validation (same BoardId plus a depth walk); export stays a per-board tree; ADR:39, ADR-0063 and per-board SignalR are untouched. Answers ADR:88 in its own words and matches #2087's non-goal wording.
- **B.** Same board only; one parent; unbounded depth with a cycle check only; no cross-project hierarchy. — *Consequence:* Matches ADR:36 most literally, but leaves UI decomposition, #2093 roll-ups and #2090 titles-only views open-ended; a depth cap can never be lowered later without breaking existing data.
- **C.** Cross-board parent within the same owner (project-like grouping via hierarchy). — *Consequence:* Uses hierarchy to fake Project. Every parent read needs two authorization checks and two archive-state checks (ADR-0063); realtime invalidation spans boards; #2087's non-goal would need explicit override.
- **D.** A, plus type-conditional nesting (e.g. Epic may not be a child; Spike may parent nothing), recorded as an explicit amendment to ADR:39. — *Consequence:* Gives tighter product semantics, but reverses "item type is independent of either relationship" — so it must be written as an amendment, and every later type addition re-opens the nesting matrix.

*Recommendation:* **A** — Dogfooding asked for epic/task/spike plus decomposition, which depth 3 covers. A cap can be raised later by changing one constant; unbounded depth can never be tightened without a data migration. Same-board keeps ADR-0063, the owner-OR-access authorization path and per-board SignalR untouched. Keep the rule type-agnostic: ADR:39 says type is independent of either relationship, and #2087 asks only for "same-owner/scope rules, cycle prevention, and a documented depth policy" — nothing in either text asks for per-type nesting legality. If per-type nesting is genuinely wanted, pick D so it is recorded as an ADR:39 amendment rather than smuggled in as an application of the ADR.

*Reversibility:* Raising the cap, or later allowing cross-board, is additive. Lowering depth or un-crossing boards is a data-breaking migration. D is hardest to loosen once UI and validation assume the matrix.

*Downstream:* #2087, #2090, #2093

#### ADR-0060.4 `parent-lifecycle` — What happens to children when their parent is deleted or archived?

*Why it matters.* #2087 acceptance line 5 requires "Deleting/archiving a parent has explicit child behavior and no silent cascade" — an explicit rule is demanded, and the ADR text supplies none. Left unrecorded, the implementing agent picks it, and the choice is visible to users and hard to change after data exists. Prerequisite (review finding): the shipped propose_archive_card apply path is a no-op today — OperationHandlerRegistry.ArchiveCardAsync builds an UpdateCardDto with IsBlocked=true and a null BlockReason, which CardService.UpdateCardAsync ignores — so a real card-archive state/handler must exist and be proven before child behavior is defined on top of it.

*Evidence.*

- #2087 acceptance: "Deleting/archiving a parent has explicit child behavior and no silent cascade"; cross-cutting row: "Export/import/deletion | Round-trip hierarchy and defined parent deletion behavior"
- docs/decisions/ADR-0060-...md:36 (adjacency list with cycle checks — no lifecycle rule stated anywhere in the ADR), :78-80 (every stage must define account deletion and proposal diff/apply behavior)
- docs/decisions/ADR-0063 (Accepted) — board archive already rejects card writes, so any cascade over cards must respect archive state
- backend/src/Taskdeck.Application/Services/Tools/ProposeArchiveCardExecutor.cs — card archive is a single-card proposal operation today; no bulk card-archive proposal op exists

*Options.*

- **A.** Detach: deleting or archiving a parent clears the child's parent pointer; children keep their IDs, board, column, history and exports. Never cascade. — *Consequence:* One rule and no new bulk operation. Deep trees can silently flatten, and re-parenting after an accidental delete is manual. NOTE (review finding): detach is a derived mutation of every child, so an archive/delete proposal on a parent must list the child detaches in proposal preview/apply/audit — today ProposeArchiveCardExecutor summarizes only the single parent operation (ProposeArchiveCardExecutor.cs:66-80); #2087 must extend preview/apply parity to those child pointer changes before A is honest.
- **B.** Block: a parent with children cannot be deleted or archived until the children are re-parented or removed; the server returns a stable 409. — *Consequence:* No data ever flattens, but it puts a new failure mode on shipped delete/archive paths — including account deletion and board archive, which ADR:78-80 requires be proven for the stage.
- **C.** Cascade-archive with explicit confirmation (never cascade delete): archiving a parent archives its subtree after the user confirms the count. — *Consequence:* Matches user intent for epic wind-down, but needs a new bulk operation with proposal preview/apply parity, realtime invalidation for N cards, and a defined partial-failure rule — real scope inside the RC window.

*Recommendation:* **A** — A adds no new user-initiated operation, but it is not proposal-surface-neutral: the child detaches are side-effect mutations that preview/apply/audit must show (see the note on A). It still satisfies #2087's "no silent cascade" literally rather than by argument. B changes the behavior of shipped delete and archive paths that ADR:78-80 already requires be re-proven; C requires a bulk multi-card operation with parity and partial-failure semantics that no shipped proposal op has. Both B and C remain available later as additive rules on top of A.

*Reversibility:* Easy — a later slice can add a confirm-and-cascade affordance or a block rule on top of detach without migrating data. Starting at C and retreating to A leaves already-archived subtrees to unpick.

*Downstream:* #2087, #2092

#### ADR-0060.5 `first-item-types` — ADR-0060 decision 4 (:89) — which WorkItemType values ship first, and what do existing cards default to?

*Why it matters.* The type enum is the first migration on the Card path (ADR compatibility Stage 2, :66-68). Removing a shipped value later is a remap migration. The LLM triage already emits a `type` that is validated then dropped.

*Evidence.*

- docs/decisions/ADR-0060-...md:66-68 (Stage 2 "smallest accepted item types"), :42 (ongoing lifecycle is not a type), :89
- #2087 problem statement: "a small distinction between tasks, epics, and spikes"; scope: "default all existing records to the accepted task-compatible type"
- docs/STATUS.md:13 — schema-v2 triage `type`/`assigneeHint`/`confidence` are validated then dropped at the service boundary (REVIVAL-11 consumer track)
- backend/src/Taskdeck.Domain/Entities/Card.cs — has IsBlocked/BlockReason, no type field; grep WorkItemType returns zero source hits

*Options.*

- **A.** Task, Epic, Spike; all existing cards default to Task; Bug/Ongoing/Decision deferred. — *Consequence:* Matches #2087's wording verbatim and the dogfooding ask; three values keep the type-agnostic hierarchy rule and the roll-ups in #2093 simple.
- **B.** Task only, plus the optional parent; add Epic/Spike when evidence lands. — *Consequence:* Smallest migration, but decomposition with no Epic type gives roll-ups no target and forces a second enum migration inside the same release band.
- **C.** Task, Epic, Spike, Bug, Decision, Ongoing. — *Consequence:* Ongoing contradicts ADR:42 (lifecycle, not type); Bug and Decision have no shipped producer or consumer; more values to police in proposals, MCP, export and UI.

*Recommendation:* **A** — Three types cover the recorded dogfooding need, are what #2087 asks for in its own words, REVIVAL-11's consumer track does NOT get a direct landing place from these three: the schema-v2 triage `type` is validated as exactly action / decision / question (CaptureTriageOutputContract), which is a different axis — REVIVAL-11 must define its own mapping (action→Task; decision/question are capture kinds, not work-item types) rather than reuse this enum. Adding a value later is an additive migration; the ADR itself keeps Ongoing as lifecycle (:42) and templates/recurrence as provenance (:40-41), so none of those belong in the enum.

*Reversibility:* Adding values is easy. Removing a shipped value needs a remap migration plus export/import handling.

*Downstream:* #2087, #1307, #2093

#### ADR-0060.6 `compat-path` — How far up ADR-0060's five-stage compatibility ladder (:63-76) does this ratification authorize execution?

*Why it matters.* Stages 1-3 are additive to the Card path; Stages 4-5 change ownership. Whatever is ratified as executable direction becomes an authority hook that ADR-0051's autonomous admission lane can cite without a further owner decision.

*Evidence.*

- docs/decisions/ADR-0060-...md:63-76 (the five stages), :78-80 (per-stage cross-cutting checklist), :113-117 (consequences)
- docs/decisions/ADR-0051-...md:3 (Status Accepted), Decision 1: "PR merging is agent-executable across all file and dependency classes ... No separate maintainer approval or owner click is required merely because of the changed path or package category"; Decision 4 admits tracked backlog autonomously
- docs/STATUS.md:133 — SerializedMigrator snapshots the SQLite file before Database.Migrate() in every host mode (a down-path safety net exists)
- backend/src/Taskdeck.Infrastructure/Migrations — most recent are 20260826173952_AddBoardConcurrencyToken and 20260826201256_AddBoardCardMutationMarker (additive-column pattern is current practice)
- backend/src/Taskdeck.Application/Services/BoardJsonExportImportService.cs:163 — `new Card(board.Id, column.Id, ...)` mints a fresh GUID on import, so "Preserve card IDs" (ADR:67, #2084 acceptance) can only bind in-place migrations, never an export/import round trip
- docs/REVIVAL_PLAN.md:61 — until ratified, Board -> Column -> Card and proposal-first automation stay unchanged

*Options.*

- **A.** Ratify Stages 1-3 as executable direction (additive columns/tables on the Card path, tested down path, card IDs preserved in place). Stages 4-5 stay Proposed direction and may be executed only after an explicit ADR-0060 amendment. — *Consequence:* #2087/#2092/#2093 can proceed once their own sub-decisions are recorded, and no issue can cite ADR-0060 as authority to begin the ownership migration — there is no per-issue route around the amendment.
- **B.** Ratify all five stages as direction now. — *Consequence:* Hands ADR-0051's agent-executable merge lane an authority hook for Project/WorkItem work with no collaboration evidence, and contradicts the ADR's own Stage 5 gating text (:74-76). Only coherent alongside project-timing B or C, because ratifying Stage 4 (:72-73) IS decision 1.
- **C.** Ratify Stage 1 only (vocabulary); keep even Stage 2 Proposed. — *Consequence:* Keeps #2087 blocked past v0.3 RC (2026-09-04) while the vocabulary alone changes nothing shipped; #2092/#2093 would also lose the additive band their ACs assume.

*Recommendation:* **A** — Stages 1-3 are the ADR's own card-compatible band and match current practice (additive migrations plus SerializedMigrator pre-migration snapshots, STATUS.md:133). Stages 4-5 are explicitly evidence-gated in the text. Keep the escape hatch single: a per-issue ruling is a materially weaker gate than an amendment under ADR-0051 Decision 1, so "amendment only" is the wording that makes the consequence true. Add one clarifying sentence to the ADR: card-ID preservation binds in-place migrations, because board JSON import already re-mints IDs (BoardJsonExportImportService.cs:163). Interlock: option B is unavailable unless project-timing B or C is also selected, since Stage 4 ratification is decision 1 restated.

*Reversibility:* Stages 2-3 are additive with a required down path. Stages 4-5 are the hard-to-reverse part and stay gated.

*Downstream:* #2087, #2092, #2093, #2094, #1772

#### ADR-0060.7 `participant-substrate` — For #2093, is Participant a new table, or is the shipped User plus board-authorization pair the participant record?

*Why it matters.* ADR compatibility Stage 3 (:69-71) admits Principal/Participant foundations "only when their issues are admitted" — #2093 IS admitted (2026-08-26 intake waiver, v0.3 Priority II) — but the ADR never says whether the first slice may reuse the shipped authorization model. This decides whether v0.3 adds identity tables before a trusted shared instance (#1772) exists.

*Evidence.*

- docs/decisions/ADR-0060-...md:46-56 (Principal/Participant/Persona/Assignment/WorkLog semantics), :69-71 (Stage 3; "WorkLog remains a later event-model decision")
- #2093 scope: "Add the narrow Principal/Participant boundary accepted in ADR-0060"; acceptance 1: "Assignment targets an authorized participant and does not grant board access" — it requires authorization, not a BoardAccess row
- backend/src/Taskdeck.Application/Services/AuthorizationService.cs:105 and :121 — CanReadBoardAsync and CanWriteBoardAsync return success on `board.OwnerId == userId` BEFORE any BoardAccess lookup; backend/src/Taskdeck.Infrastructure/Repositories/BoardRepository.cs:240 — `board.OwnerId == userId || board.BoardAccesses.Any(access => access.UserId == userId)`
- docs/STATUS.md:42 — "a board owner deliberately holds no `BoardAccess` row"; docs/STATUS.md:116 — BoardDto.CanWrite is "Owner/Admin/Editor plus board *ownership*, the case a `BoardAccess`-derived signal gets wrong because owners hold no access row"
- backend/src/Taskdeck.Domain/Entities/BoardAccess.cs:13-16 — (BoardId, UserId, Role, GrantedBy) is already a context-scoped membership record; ApiKey and AgentProfile exist as non-human identity
- docs/strategy/PRODUCT_DIRECTION.md:92-93 (P8: real principals before speculative multi-tenancy)

*Options.*

- **A.** v0.3: User is the Principal; participation is the shipped board-authorization set — Board.OwnerId OR a BoardAccess row. Assignment references (CardId, UserId) and is validated through that same owner-or-access check, never against a BoardAccess row alone. No new identity tables until #1772 evidence. — *Consequence:* #2093 becomes an additive Assignment plus Estimate migration; invites and pending externals are out of scope; a later rename to Participant is a table rename, not a semantic change. Assigning a card to the board's own owner works, which the BoardAccess-row-only formulation would have forbidden on every solo board.
- **B.** Introduce Principal and Participant tables in v0.3, migrating BoardAccess onto them. — *Consequence:* Touches the authorization path (AuthorizationService.cs:105,121; BoardRepository.cs:240; the CanWrite lanes at STATUS.md:116) during the RC window — the highest-risk seam, with zero shipped collaboration evidence to justify it.
- **C.** Defer #2093 entirely to the small-team collaboration alpha (OUTSTANDING_TASKS.md:14, after #1772). Not to be confused with ADR-0060 compatibility Stage 2. — *Consequence:* Loses the v0.3 assignment/estimate roll-ups the maintainer waived intake for, and leaves the model untouched.

*Recommendation:* **A** — The ADR's semantics (Participant is context-scoped membership; Assignment carries responsibility, not authority) are already satisfied by the shipped owner-or-access check plus a foreign-key rule. The rule must be phrased as "resolves through the authorization check", not "must match an existing BoardAccess row": owners deliberately hold no access row (STATUS.md:42, :116; AuthorizationService.cs:105,121), so a row-only rule would make a solo user unable to assign any card on their own board. P8 puts accountability before tables, and ADR-0061 is still Proposed, so the shared instance that would justify invite/pending participants does not exist. Recording A gives #2093 an unambiguous seam and leaves the authorization lanes untouched.

*Reversibility:* Moderate — Assignment rows referencing UserId can be re-pointed to a Participant table by one migration. Starting at B is hard to unwind because it moves the authorization path itself.

*Downstream:* #2093, #1772, #2091

#### ADR-0060.8 `relation-scope` — May a #2092 typed edge connect cards that live on two different boards?

*Why it matters.* This is distinct from identity: a same-owner cross-board relates-to edge changes nothing about what a work item IS, but it decides #2092's server-side scope validation, its dangling-edge export/deletion rule, and whether realtime invalidation must span boards. #2092's non-goals exclude "multi-board identity" but say nothing about cross-board edges, so acceptance without a ruling leaves it to the implementing agent.

*Evidence.*

- #2092 scope: "Validate endpoints, direction, scope, duplicate edges, and forbidden self-links server-side"; acceptance: "Export/import/deletion define dangling-edge behavior and round-trip typed links" and "Permissions, concurrency, realtime invalidation, API, MCP, and UI tests pass"
- #2092 non-goals: "No parent hierarchy, graph database, cross-project rules, templates, recurrence, or multi-board identity" — cross-board edges are not named either way
- #2092 dependency line: "Depends on accepted ADR-0060 semantics" — the five admitted edge types (blocks, relates to, duplicates, spawned from, depends on) are already at ADR:37-38, so acceptance alone releases this issue
- docs/decisions/ADR-0060-...md:37-38 (WorkRelation typed edges), :69-71 (Stage 3 admits typed relations when their issues are admitted)
- docs/decisions/ADR-0063 (Accepted) — archived boards reject card writes, so a cross-board edge needs archive-state evaluation on both endpoints
- backend/src/Taskdeck.Application/Services/BoardJsonExportImportService.cs:163 — board JSON export/import is per-board and re-mints card IDs, so a cross-board edge cannot round-trip through it

*Options.*

- **A.** Same board only in the first slice: both endpoints must share a BoardId; a cross-board endpoint fails server-side with a stable error. — *Consequence:* Scope validation is one equality check; dangling-edge rules stay inside one board's export; realtime invalidation stays per-board; ADR-0063 archive state is evaluated once. No cross-board mirror for dogfooding.
- **B.** Allow cross-board edges when both boards have the same owner. — *Consequence:* Gives a cross-board mirror with no identity change, but every edge read needs two authorization evaluations and two archive-state checks, and per-board export must define what happens to an edge whose far endpoint is not in the exported board.
- **C.** Allow cross-board edges wherever the acting principal can read both boards (owner-or-access on each endpoint). — *Consequence:* Most useful and most exposed: an edge can become unreadable when access is revoked on one side, and BoardsHub publishes nothing on access change (STATUS.md:42), so there is no event to invalidate the far side.

*Recommendation:* **A** — A is the only option whose validation, export and realtime behavior are already provable with the shipped per-board machinery, and it is what #2092's acceptance criteria can be satisfied against inside the v0.3 window. B and C are additive later — widening the scope predicate does not migrate data — whereas starting at B or C and retreating requires deleting user-visible edges. Record this separately from multi-board-identity so the option A there is not read as either granting or forbidding cross-board edges.

*Reversibility:* Easy to widen (A -> B -> C is a predicate change). Narrowing after edges exist destroys user data.

*Downstream:* #2092, #2087

#### ADR-0060.9 `custom-fields-timing` — ADR-0060 decision 5 (:90) — do generic custom fields enter v0.3 or later, and is that answer recorded in ADR-0060 or delegated?

*Why it matters.* OUTSTANDING_TASKS.md:14 puts custom-field timing in the SAME ratification item as Project timing, multi-board identity, hierarchy and first types. #2094's dependency line is "Blocked on the dedicated ADR-0062 decision issue and accepted ADR-0060 scope" — it needs an answer from both. Delegation is itself a choice, and ADR-0060:82-94 will keep listing an unrecorded decision if nothing is written.

*Evidence.*

- docs/decisions/ADR-0060-...md:90 (decision 5), :93 ("no generic fields" is the safer default), :116-117 (custom fields "remain deferred until separately admitted")
- docs/decisions/ADR-0062-...md:3 (Status Proposed), :12 ("It does not describe shipped behavior or authorize implementation before the canonical work-model decision in ADR-0060 is accepted"), :75-79 ("Human decisions required", including "Do generic custom fields belong in v0.3 or after the collaboration alpha?")
- #2091 decision list item 1: "whether minimal generic custom fields enter v0.3"; acceptance: "ADR-0062 stays Proposed until the maintainer records answers here"
- #2094 dependency line: "Blocked on the dedicated ADR-0062 decision issue and accepted ADR-0060 scope. v0.3 conditional, Priority III."
- OUTSTANDING_TASKS.md:14 — one open item covering "Project timing, multi-board identity, hierarchy boundaries, first item types, custom-field timing, actual-time-tracking fit"

*Options.*

- **A.** Rule it here: no generic custom fields in v0.3. ADR-0060 records "later", #2094 stays conditional and blocked, and #2091/ADR-0062 keep only the design questions ADR-0060 never asked (ownership scope, value typing, deletion policy). — *Consequence:* ADR-0060's decision list closes cleanly, and #2094 has an ADR-0060-side answer even before #2091 is answered. If the maintainer later wants fields in v0.3, it takes an ADR-0060 amendment plus a #2091 ruling.
- **B.** Delegate: amend ADR-0060 decision 5 to read "delegated to #2091 / ADR-0062" so the answer lives in exactly one place. — *Consequence:* No duplicate rulings to keep in sync, but nothing is decided today: #2094 stays blocked on both gates, and ADR-0062:12 makes clear it authorizes no implementation until ADR-0060 is accepted, so the ordering must be stated or the two documents read as waiting on each other.
- **C.** Defer with a named gate: neither rule nor delegate; ADR-0060 records "re-decide after the small-team collaboration alpha (post-#1772)". — *Consequence:* Honest about evidence, but leaves OUTSTANDING_TASKS.md:14 partly unresolved and #2094 with no path to admission inside v0.3.

*Recommendation:* **A** — ADR-0062:12 already makes ADR-0060 acceptance the gate on any field implementation, so ADR-0060 has to carry a scope answer regardless of what #2091 later says; recording "later" here is the smallest statement that makes #2094's second gate determinate. It does not pre-empt #2091: that issue's remaining questions (definition ownership scope, deletion policy preserving historical values, export compatibility) are design questions ADR-0060 never poses. Note for the maintainer: this is a timing ruling, not a rejection — ADR:116-117 already says fields remain deferred until separately admitted, so A makes the ADR internally consistent rather than adding a new constraint.

*Reversibility:* Easy — an amendment plus a #2091 ruling can admit a minimal field slice at any time; nothing is built either way.

*Downstream:* #2094, #2091

#### ADR-0060.10 `time-tracking-fit` — ADR-0060 decision 6 (:91) — does actual time tracking fit the product thesis, and where is that recorded?

*Why it matters.* This is the last of ADR-0060's six numbered decisions and the last clause of OUTSTANDING_TASKS.md:14. It is duplicated verbatim in ADR-0062:79. Unanswered, ADR-0060:82-94 still reads as an unfinished decision list even after ratification, and WorkLog's status stays ambiguous for #2093's roll-up wording.

*Evidence.*

- docs/decisions/ADR-0060-...md:91 (decision 6), :55-56 (WorkLog is an immutable activity record, separate from assignment/estimate/column/elapsed time), :57-58 (estimate, remaining effort, actual work logged, elapsed cycle time, lead time, relative size and capacity are different values), :70-71 ("WorkLog remains a later event-model decision"), :93 ("no ... time tracking" is the safer default), :116-117
- docs/decisions/ADR-0062-...md:79 — "Is actual time tracking part of Taskdeck's context-to-action thesis or unwanted project-management weight?" (duplicate of ADR-0060 decision 6); :3 Status Proposed; :12 no implementation before ADR-0060 acceptance
- #2093 non-goals: "No WorkLog, time tracking, capacity forecasting, duration formula, auto-scheduling, or generic custom fields"; acceptance: "Roll-ups state that they are assignment/estimate totals, not historical activity or capacity"
- #2091 decision list item 3: "whether actual time tracking fits Taskdeck's product thesis"
- docs/analysis/2026-08-26-v012-dogfooding-reconciliation.md:63 — "detailed time tracking, capacity forecasting" are post-v0.3 candidates pending decisions and evidence
- docs/strategy/PRODUCT_DIRECTION.md:86-87 (P5 "Context should become movement"), :96-97 (P10 broad vision does not admit broad scope)

*Options.*

- **A.** Record "not in the thesis for now": no WorkLog and no actual-time capture before the collaboration alpha; only the built-in estimate in #2093 ships. ADR-0060 states this and notes ADR-0062:79 is the same question, answered here. — *Consequence:* Closes decision 6 and makes ADR:70-71 ("WorkLog remains a later event-model decision") and #2093's non-goals consistent with the decision list. #2091 keeps the deletion/scope design questions only.
- **B.** Delegate decision 6 to #2091 / ADR-0062:79 and amend ADR-0060 to point there. — *Consequence:* One home for the question, but it must be sequenced explicitly: ADR-0062:12 says ADR-0062 authorizes no implementation before ADR-0060 is accepted, and #2091's acceptance keeps ADR-0062 Proposed until the maintainer records answers there — so ADR-0060 ratification cannot itself wait on the delegate.
- **C.** Record "fits the thesis, deferred in schedule": accept time tracking as directionally in-scope but gate implementation behind an admitted issue and evidence. — *Consequence:* Gives future work an authority hook under ADR-0051's admission lane for a surface with no shipped producer or consumer, and sits awkwardly against ADR:93's safer default and P10.

*Recommendation:* **A** — Every shipped and admitted artefact already behaves as if the answer is no: ADR:70-71 defers WorkLog, ADR:93 names no-time-tracking as the safer default, #2093's non-goals exclude it outright, and the reconciliation (:63) lists detailed time tracking as a post-v0.3 candidate. A makes the record say what the plan already does. Prefer A over B because the delegate is Proposed and its own implementation gate is ADR-0060 acceptance (ADR-0062:12) — delegating leaves the decision resting on a document that cannot authorize anything until this one is ratified. B is still workable if the maintainer wants a single home, provided the ADR states the ordering: answer #2091 after ADR-0060 is Accepted, not before.

*Reversibility:* Easy — nothing is built; admitting a WorkLog event model later is an additive decision plus an admitted issue.

*Downstream:* #2091, #2093

<details><summary>Sources read for this brief</summary>

- docs/decisions/ADR-0060-canonical-work-model-and-compatibility-path.md — full file re-read with line numbers at 927236bd0 (118 lines); all cited anchors re-verified: :3 status, :24-32 vocabulary, :36, :39, :40-42, :46-58, :60-61, :63-76 stages, :66-68 Stage 2, :69-71 Stage 3, :72-73 Stage 4, :74-76 Stage 5, :78-80 checklist, :82-94 decisions, :86-91 the six, :93 safer defaults, :96-109 alternatives (:101-103 immediate rewrite), :113-117 consequences
- docs/decisions/ADR-0062-custom-fields-aggregates-and-threshold-rules.md:1-20 and :70-85 — :3 Proposed, :12 implementation gate, :75-79 human decisions (:77 fields timing, :79 time tracking)
- docs/decisions/ADR-0051-autonomous-backlog-admission-and-agent-executable-merge.md:1-48 — :3 Accepted, Decision 1 (agent-executable merge across all file/dependency classes), Decision 4 (autonomous admission)
- gh issue view 2084, 2087, 2091, 2092, 2093, 2094 --json number,title,state,milestone,body — full bodies read; dependency and acceptance lines quoted verbatim above
- OUTSTANDING_TASKS.md:10-20 (line 14 is the single ratification item naming all six decisions plus the #2012 question, the fixed release targets, and the #2092-#2094 intake waiver)
- docs/REVIVAL_PLAN.md:44-62 — :47 is the v0.2 row (final 2026-09-01), :48 is the v0.3 row (RC 2026-09-04, final 2026-09-08 or 09), :59 "Later" allocation, :61 "Until then the shipped Board -> Column -> Card ... remain unchanged" (draft's :47 date citation corrected to :48)
- docs/strategy/PRODUCT_DIRECTION.md:82-98 and :100-110 — P4 at :84-85, P5 at :86-87, P8 at :92-93, P10 at :96-97; horizon table rows: v0.2 at :104, v0.3 "Open Beta + Accountable Agents" at :105, v0.4 at :106, Later at :107 (draft's :107 citation for v0.3 corrected to :105)
- docs/analysis/2026-08-26-v012-dogfooding-reconciliation.md:60-68 — grep-confirmed that "one canonical item on several boards" is at :63 (draft cited :64)
- docs/STATUS.md — grep-only, section-read: :13 (triage type/assigneeHint/confidence validated then dropped), :42 ("a board owner deliberately holds no `BoardAccess` row"; BoardsHub publishes nothing on access change), :116 (BoardDto.CanWrite is Owner/Admin/Editor plus ownership "because owners hold no access row"), :133 (SerializedMigrator pre-migration snapshot)
- backend/src/Taskdeck.Application/Services/AuthorizationService.cs:95-125 — CanReadBoardAsync owner short-circuit at :105, CanWriteBoardAsync owner short-circuit at :121, both before any BoardAccess lookup
- backend/src/Taskdeck.Infrastructure/Repositories/BoardRepository.cs:234-248 — BuildReadableQuery predicate `board.OwnerId == userId || board.BoardAccesses.Any(access => access.UserId == userId)` at :240
- backend/src/Taskdeck.Domain/Entities/Card.cs:8-20 and :92-102 — BoardId at :11, ColumnId at :14, MoveToColumn at :95-99 (ColumnId + position only); no type or parent field
- backend/src/Taskdeck.Domain/Entities/BoardAccess.cs:10-20 — BoardId/UserId/Role/GrantedBy at :13-16
- backend/src/Taskdeck.Application/Services/BoardJsonExportImportService.cs:158-168 — `new Card(board.Id, column.Id, ...)` at :163 mints a fresh GUID on import
- backend/src/Taskdeck.Api/Mcp/WriteTools.cs — McpServerTool names are create_card (:80), move_card (:184), update_card (:243), archive_card (:334), plus create_capture (:386) and create_column (:428); no `propose_*`-named MCP tool exists (correcting the draft's tool-name claim). Application-layer executors are backend/src/Taskdeck.Application/Services/Tools/Propose{Create,Update,Move,Archive}CardExecutor.cs, ProposeBulkMoveExecutor.cs, ProposeCreateColumnExecutor.cs
- backend/src/Taskdeck.Infrastructure/Migrations — 20260826173952_AddBoardConcurrencyToken and 20260826201256_AddBoardCardMutationMarker are the most recent (additive pattern)
- grep -rn WorkItemType backend/src frontend/taskdeck-web/src = 0 matches; grep -rn 'ParentCardId|ParentId' = 17 matches, all binary DLLs under bin/Debug and bin/Release (zero source hits)
- git rev-parse origin/main = 927236bd0304e9dfae59a7116394e4fcb7b0ec07 — every code and line citation above was taken at this head; nothing from integration/v0.3.0 is cited or treated as shipped

</details>

## ADR-0061

### Trusted Shared Instance and Managed SaaS Boundary

**Status line:** docs/decisions/ADR-0061-trusted-shared-instance-and-managed-saas-boundary.md:3 — "- **Status**: Proposed" (Date 2026-08-26; docs/decisions/INDEX.md:65 row also says Proposed). Last touched c07b765c5 ("Clarify proposed model decision boundaries", 2026-08-26). Byte-identical on origin/main 927236bd0 and origin/integration/v0.3.0 (`git diff --stat` between the two for this path is empty), so nothing in this brief depends on v0.3-only work.

**Context.** ADR-0061 splits collaboration hosting into three milestones: (1) a trusted private shared instance — "one invite-only container for a few known users" (ADR:25) — owned by #1772; (2) a dependable small-team alpha (ADR:42-48); (3) managed public SaaS as a separate later product needing its own accepted decision (ADR:50-55). The ADR text settles: SQLite stays (PostgreSQL is explicitly not a prerequisite, ADR:57-59; ADR-0023/0025 stay parked), one instance and one volume with WAL and database-authoritative reconnect (ADR:30-31), InviteOnly-during-onboarding then Closed only after every intended account exists (ADR:32), HTTPS/SignalR proof (ADR:33), backup of both the SQLite file and the connector-encryption key (ADR:34), one real restore drill (ADR:35), two-user walkthroughs (ADR:36), BYO-or-operator-funded LLM credentials with cost and egress disclosure (ADR:37), and general optimistic concurrency deferred to Stage 2 (ADR:47-48). It does NOT settle MFA — the file contains no MFA/TOTP text at all; MFA-off for this instance is settled on #1653 (comment 2026-08-19) and #1772 (comment 2026-08-23, citing `MfaPolicySettings.EnableMfaSetup` defaulting to false), not by ratifying this ADR. Left open by the ADR itself (ADR:61-70): the operating model, who pays for LLM usage, and whether #2012 blocks a public hosted path; left open by OUTSTANDING_TASKS.md:21 (CL-1): private access boundary and known users, host, budget/alerts/cost owner, LLM payer/egress posture, backup retention/destination, connector-key custody, restore target. Nothing has been deployed: #1772 and #1777 have no evidence since filing on 2026-08-19, and the recorded reason is sequencing, not engineering — #1772 comment 2026-08-23: "Blocked on the solo sprint, not on code — there is no engineering prerequisite left that this pass could identify." Shipped on main at 927236bd0: registration modes Open/InviteOnly/Closed (RegistrationSettings.cs:5-16, RegistrationPolicyService.cs:92-105) with the shipped default `RegistrationMode.Open` (RegistrationSettings.cs:15); board roles and server-computed `CanWrite` (STATUS.md:116, :151); grant board access by email/username (#1771, shipped as PR #1774 per #1772 comment); board-scoped SignalR (`BoardsHub`, STATUS.md:466); WAL-safe pre-migration auto-backup via the SQLite online backup API (STATUS.md:132-133); `GET /api/privacy/egress` (EgressDisclosureController.cs:8-25); managed-key policy with quota/budget ceilings (docs/security/MANAGED_KEY_USAGE_POLICY.md:10-27, ceiling key at :27); deploy/render.yaml and deploy/railway.toml.

**If ratified unmodified.** Accepting the text as written commits to: three separate milestones (trusted instance → small-team alpha → managed SaaS needing its own accepted decision, ADR:55); single-instance SQLite with WAL and one volume, InviteOnly→Closed, verified HTTPS/SignalR, backup of both DB and connector key, one restore drill, two-user walkthroughs (ADR:30-37); no PostgreSQL prerequisite (ADR:57-59); general optimistic concurrency deferred to Stage 2 (ADR:47-48); and the ADR:69-70 default — maintainer-operated trusted self-hosting, BYO provider keys "where practical" (not buildable today, so in practice operator-funded or live providers off), and no managed-service commitment before #2012 and retention evidence. It does NOT settle MFA (no MFA text in the file; that is settled on #1653/#1772 comments), and it leaves every CL-1 operational answer unrecorded — user set, private-access perimeter, host, budget/alerts/cost owner, LLM payer/egress posture, backup retention/destination, key custody, restore target — so OUTSTANDING_TASKS.md:21 and all four of #1772's blocking checkboxes stay open. Two further things ratification alone does not do: it does not lift the deployment gate, because #1772's 2026-08-23 comment records the blocker as the solo dogfooding sprint and the q-8 sequencing ruling ("the 2026-09-01 checkpoint is a floor for that assessment, not an eligibility date for this deployment"); and it does not resolve the status circularity between RELEASE_TRUST_AND_DISTRIBUTION.md:77 and #1772's fourth checkbox — that is sub-decision `adr-status-disposition`, which should be answered first. Suggested order for one sitting: (1) adr-status-disposition; (2) the CL-1 block — access-boundary, private-access-perimeter, host-selection, budget-alerts-cost-owner, llm-cost-ownership, backup-retention-destination, connector-key-custody, restore-target; (3) operating-model and 2012-blocks-managed-path, both of which are confirm-the-ADR-default with no new work implied. Answering (1) and (2) discharges OUTSTANDING_TASKS.md:14's ADR-0061 line and CL-1; (3) can be a single "confirm default" ruling.

**Cross-ADR dependencies.**

- ADR-0050 / #2012 — commercial-model gate for Stage 3; #2012's stop criterion is two-part (model decision AND contribution-policy answer), so it is not lifted by a single ruling
- ADR-0055 — OpenAI-only, single deployment-global key; drives llm-cost-ownership. Per-user BYOK needs the separate #1879 decision (MANAGED_KEY_USAGE_POLICY.md:17)
- ADR-0009 (localStorage tokens, #1644) and the MFA-at-rest gap (#1653) — both risk acceptances are scoped by their own wording to 'a private two-person self-hosted instance... tunnel-fronted HTTPS'. Widening the USER SET (access-boundary B) and changing the HOST to a PaaS (host-selection B/C) each fall outside that wording and require re-recording
- ADR-0023 / ADR-0025 — PostgreSQL and the Redis SignalR backplane stay parked; ADR-0061:57-59 makes PostgreSQL explicitly not a prerequisite for the single-instance proof (a Redis backplane ships but is off unless configured, STATUS.md:466)
- ADR-0044 + REVIVAL_PLAN.md:31,:35 — a managed hosted instance is a candidate paid surface 'not built yet' and the intended first paid product after retention evidence; ADR-0061 Stage 3 must not pre-empt it
- ADR-0057 (INDEX.md:61) and ADR-0059 (INDEX.md:63) — the precedent form for the recommended status disposition: Accepted with a dated maintainer-ruling qualifier
- ADR-0060 (#2084) and ADR-0062 (#2091) — same ratification batch (OUTSTANDING_TASKS.md:14), independent content, shared sitting; both still Proposed (INDEX.md:64, :66)

| # | Sub-decision | Recommended |
| --- | --- | --- |
| 1 | `adr-status-disposition` — What status does ADR-0061 take now — stay Proposed, "Accepted as direction only, evidence pending", or Accepted with #1772's fourth checkbox amended? | **B** |
| 2 | `access-boundary` — Which named accounts are allowed on the private instance, and what registration sequence enforces it? (CL-1: 'the private access boundary and known users' — user set only; the network perimeter is the next sub-decision.) | **A** |
| 3 | `private-access-perimeter` — What independent private-access layer sits in front of the instance, if any? | **A** |
| 4 | `host-selection` — Where does the trusted instance run: keep the q-1 ruling (self-host + tunnel), or move to Render — and if Render, before or after #1504? | **A** |
| 5 | `budget-alerts-cost-owner` — What monthly budget, alert threshold, and named cost owner apply to infrastructure plus LLM spend? | **A** |
| 6 | `llm-cost-ownership` — Who pays for LLM calls on the shared instance, and how is egress disclosed to the collaborator? | **A** |
| 7 | `backup-retention-destination` — How often is the SQLite volume backed up, how long are copies kept, and where do they live? | **A** |
| 8 | `connector-key-custody` — Where is `Connectors:EncryptionKey` held and recovered from, separately from the database backups? | **A** |
| 9 | `restore-target` — What is the 'clean target' for the one required restore drill? | **A** |
| 10 | `operating-model` — Is the v0.3 proof maintainer-operated trusted self-hosting, or the start of a managed service? (Confirm the ADR default — no new work either way.) | **A** |
| 11 | `2012-blocks-managed-path` — Does the open commercial/licensing decision (#2012) block any future public managed-service path? (Confirm the ADR default — no new work either way.) | **A** |

#### ADR-0061.1 `adr-status-disposition` — What status does ADR-0061 take now — stay Proposed, "Accepted as direction only, evidence pending", or Accepted with #1772's fourth checkbox amended?

*Why it matters.* This is the one choice that mechanically discharges the OUTSTANDING_TASKS.md:14 ratification line, and it sits in a circular gate: docs/ops/RELEASE_TRUST_AND_DISTRIBUTION.md:77 says deployment stays blocked until the maintainer "records the required decisions and accepts the ADR", while #1772's fourth blocking checkbox says the ADR "moves from Proposed only after the evidence exists". Deployment needs an accepted ADR; acceptance needs deployment evidence. Answer this first — every other sub-decision below is content the chosen status records.

*Evidence.*

- docs/decisions/ADR-0061-...md:3 Status Proposed; docs/decisions/INDEX.md:65 row Proposed, 2026-08-26
- docs/ops/RELEASE_TRUST_AND_DISTRIBUTION.md:77 — 'deployment remains blocked until the maintainer records the required decisions and accepts the ADR'
- #1772 body, 'Decisions that block deployment', 4th box — 'ADR-0061 records those answers and moves from Proposed only after the evidence exists.'
- OUTSTANDING_TASKS.md:14 — 'Ratify the remaining dogfooding decision batch... ADR-0061 by #1772'
- Precedent for a qualified acceptance: docs/decisions/INDEX.md:61 — ADR-0057 'Accepted (maintainer ruling 2026-08-24 with an openness caveat; review-first operative until separately gated implementation)'; INDEX.md:63 — ADR-0059 'Accepted (maintainer ruling 2026-08-24, recorded on #1992)'

*Options.*

- **A.** Stay Proposed; record the CL-1 answers as a dated ruling on #1772 only. — *Consequence:* RELEASE_TRUST_AND_DISTRIBUTION.md:77 keeps deployment blocked and OUTSTANDING_TASKS.md:14 stays open; the operational answers exist but nothing is unblocked.
- **B.** "Accepted as direction only, evidence pending" — status line and INDEX row carry the dated qualifier on the ADR-0057 precedent (INDEX.md:61); Stage 1 deployment stays gated on #1772's remaining human acts and the solo-sprint sequencing. — *Consequence:* Discharges the ratification line and the 'accepts the ADR' clause at :77 without claiming evidence that does not exist. Requires reading #1772's fourth checkbox as governing the later move to unqualified Accepted, and saying so on the issue.
- **C.** Accepted outright, with #1772's fourth checkbox reworded so evidence gates the Stage 1 acceptance criteria rather than the ADR status. — *Consequence:* Strongest signal, but records an unqualified Accepted for a milestone with zero deployment evidence; contradicts the checkbox as written unless it is edited in the same act.

*Recommendation:* **B** — B is the only option that breaks the circularity without either stalling (A) or overstating (C). The repository already has two precedents for exactly this qualified form (INDEX.md:61, :63), and the qualifier keeps the honest fact visible: no instance exists, and #1772 comment 2026-08-23 records the blocker as the solo sprint, not the ADR. Whichever is chosen, say on #1772 how the fourth checkbox is now read.

*Reversibility:* Easy — one status line plus one INDEX row; the qualifier can be tightened to plain Accepted once Stage 1 evidence lands.

*Downstream:* #1772, #1777, OUTSTANDING_TASKS.md:14 (ratification batch item, tracked alongside ADR-0060/#2084 and ADR-0062/#2091)

#### ADR-0061.2 `access-boundary` — Which named accounts are allowed on the private instance, and what registration sequence enforces it? (CL-1: 'the private access boundary and known users' — user set only; the network perimeter is the next sub-decision.)

*Why it matters.* The #1644 (localStorage JWT) and #1653 (MFA secrets) risk acceptances are scoped by their own wording to a two-person self-hosted instance; widening the user set widens accepted risk silently. Separately, the shipped default is `RegistrationMode.Open` (RegistrationSettings.cs:15), so the self-host/Docker path must explicitly set `Auth__Registration__Mode` or #1772's AC 'no public signup is exposed' fails on day one.

*Evidence.*

- #1644 comment 2026-08-19: acceptance scoped to 'a private two-person self-hosted instance (maintainer + one invited friend, invite-only registration, tunnel-fronted HTTPS)'; #1644 comment 2026-08-23 re-confirms the trigger stays armed for any public/open-registration hosting
- #1653 comment 2026-08-19: MFA left disabled on the #1772 instance so no TOTP secret is ever persisted
- ADR-0061:25 'a few known users'; ADR-0061:32 InviteOnly during onboarding, Closed only after every intended account exists (#1325 sets no user count — its ACs specify a runbook, privacy note, feedback path, and activation metrics)
- RegistrationSettings.cs:5-16 (Open=0, InviteOnly=1, Closed=2; `Mode` defaults to `RegistrationMode.Open` at :15); RegistrationPolicyService.cs:92-105 availability behaviour per mode
- deploy/render.yaml:52-53 sets `Auth__Registration__Mode: Closed`; deploy/railway.toml:12-13 instructs the operator to set `Auth__Registration__Mode=Closed` and mint the first-owner invite in a private shell
- #1771 (grant board access by email/username) shipped as PR #1774 and closed 2026-08-19 — per #1772 comment 2026-08-23, the invite-a-second-person UX gap is gone

*Options.*

- **A.** Exactly two named accounts (maintainer + one collaborator); InviteOnly only while the second account is created, then Closed; registration mode set explicitly at deploy time. — *Consequence:* Stays inside the literal wording of the #1644 and #1653 acceptances; smallest walkthrough set; no re-recording of accepted risk.
- **B.** 'A few known users' beyond two (the friends-and-family group #1325 anticipates), same InviteOnly → Closed sequence. — *Consequence:* Exceeds the two-person wording of #1644/#1653, so both acceptances must be re-recorded with a dated note naming the larger group; #1325's privacy note (AC2) should exist first since more people's data lands in one maintainer-readable DB.

*Recommendation:* **A** — A is exactly what the recorded risk acceptances cover and what ADR:36's two-user walkthroughs need; B is a coherent Stage 1.5 once #1325's runbook and privacy note exist. The maintainer must still name the collaborator — that is #1772's first blocking checkbox and is human-only. Whichever is chosen, record that the deployment sets registration mode explicitly, because the code default is Open. Rejected without an option row: leaving InviteOnly open indefinitely for ad-hoc invites — ADR:32 permits Closed only after every intended account exists, i.e. the sequence terminates.

*Reversibility:* Easy for the mode itself (config change). Widening the user set requires a dated re-recording on #1644 and #1653 first, so treat that as a decision, not a config edit.

*Downstream:* #1772, #1644, #1653, #1325

#### ADR-0061.3 `private-access-perimeter` — What independent private-access layer sits in front of the instance, if any?

*Why it matters.* CL-1 asks for 'the private access boundary'. Registration mode plus an unlisted URL is not one: the programme doc states it explicitly, and the shipped app exposes login, health, and SignalR endpoints to anyone who reaches the URL. This is a distinct choice from the user set and the brief must not collapse it into registration mode.

*Evidence.*

- docs/spikes/TASKDECK_RELEASE_TRUST_DISTRIBUTION_AND_CLOUD_PROGRAMME.md:562 — 'add an independent private-access layer or provider access control. Registration mode is not a network perimeter'
- programme:561 keeps InviteOnly→Closed as a separate bullet from :562, i.e. the doc treats them as two requirements
- #1644 comment 2026-08-19 names 'tunnel-fronted HTTPS' as part of the accepted posture
- ADR-0061:33 requires verified HTTPS and SignalR/WebSocket proxy behaviour — whatever layer is chosen must not break WebSocket upgrade
- docs/ops/RELEASE_TRUST_AND_DISTRIBUTION.md:78 lists 'private access' as one of the boundary conditions of an authorized proof
- #1772 AC: 'Registration changes follow the InviteOnly → Closed sequence and no public signup is exposed'

*Options.*

- **A.** Tunnel with an identity/access policy in front of it (Cloudflare Access or equivalent): only the two named identities reach the origin at all. — *Consequence:* Satisfies programme:562 as a real perimeter; adds one provider account and one policy to maintain; SignalR/WebSocket behaviour through the policy must be proved as part of ADR:33's verification.
- **B.** Provider-side access control or IP allowlist on the chosen host. — *Consequence:* Works only where the host offers it and where both users have stable addresses; couples the perimeter to the host, so a later migration re-opens this decision.
- **C.** No independent layer: registration mode plus an unlisted HTTPS URL, recorded as an explicitly accepted risk. — *Consequence:* Contradicts programme:562 unless recorded as a knowing exception; the app's own auth is then the only barrier, which is the posture #1644/#1653 accepted for two people — so it is defensible only at access-boundary option A and must be written down as such.

*Recommendation:* **A** — A is the only option that satisfies programme:562 on its own terms and it composes with the recommended host (self-host + tunnel), where the tunnel provider supplies the access policy at no extra hop. C is survivable for a two-person instance but must be recorded as an accepted risk against :562 rather than left implicit — which is what the current draft plan does by default. If C is chosen, verify that no unauthenticated surface leaks (health endpoints, Swagger) before inviting the collaborator.

*Reversibility:* Easy — a provider-side policy, not an application change; but note that switching hosts re-opens option B.

*Downstream:* #1772, #1777, #1644

#### ADR-0061.4 `host-selection` — Where does the trusted instance run: keep the q-1 ruling (self-host + tunnel), or move to Render — and if Render, before or after #1504?

*Why it matters.* CL-1 asks the maintainer to 'accept Render or an explicit alternative'. But the record is explicit that the absence of an instance is not caused by the host choice: #1772 is 'Blocked on the solo sprint, not on code', and #1777 is parked as 'two steps out of order'. Choosing a host does not lift that gate, and moving to a PaaS moves the deployment outside the literal wording of the #1644/#1653 acceptances.

*Evidence.*

- #1772 comment 2026-08-23: 'Host choice is ruled (walkthrough q-1, 2026-08-19): self-host + tunnel first, Render migration deferred to #1777'
- #1772 comment 2026-08-23: 'Blocked on the solo sprint, not on code — there is no engineering prerequisite left that this pass could identify.' Same comment, q-8 re-ruling: the ≥10-day sprint started 2026-08-22 solo and 'extends to a collaborator only once the collaboration surface works or the product is macOS-releasable. The 2026-09-01 checkpoint is a floor for that assessment, not an eligibility date for this deployment.'
- #1777 comment 2026-08-23: 'parked, not closed — managed hosting is post-retention work and this is two steps out of order... A Render migration before the tunnel instance has ever run would be migrating something that does not exist yet.'
- #1777 'Depends on': '#1504: any protected delivery path must be real and rehearsed before activation'. #1504 is OPEN with no milestone; its unchecked ACs include the branch/tag policy confirmation and 'A non-deploying rehearsal proves an authorized run waits for the configured reviewer gate'
- deploy/render.yaml:100 `autoDeploy: true`, :101 `branch: main`, :97 `plan: starter`, :94 `numInstances: 1`, :88 `sizeGB: 1`
- programme:556-564 (keep Render primary; one paid service + one disk; numInstances=1; :563 'deploy only from an exact tested image/version after CI and a real protected environment gate'); :566 'The current repository Blueprint requests a 1 GB disk and auto-deploys from `main`; the programme should reconsider direct auto-deploy in favour of deploy-after-CI and protected approval'; :588 'Do not churn from the existing Render path merely because another platform has a lower headline minimum'; :572 Railway US$5 monthly minimum
- docs/ops/RELEASE_TRUST_AND_DISTRIBUTION.md:98 — Render default is one starter service plus a 1 GB disk, 'roughly USD 7/month plus storage/egress', a 2026-08-27 planning input to re-verify at purchase
- docs/strategy/PRODUCT_DIRECTION.md:105 — 'Small-team collaboration proof (trusted shared instance, macOS golden path) begins here **and completes post-launch**'; :107 hosted/commercial exploration is 'Later... only after retention evidence'
- #1644 comment 2026-08-19 scopes the accepted security posture to a 'private two-person **self-hosted** instance... **tunnel-fronted HTTPS**'
- OUTSTANDING_TASKS.md:21 CL-1 — 'Do not create or bill an account until authorized'

*Options.*

- **A.** Keep q-1: self-host + tunnel on maintainer hardware; #1777 stays parked as the later migration. — *Consequence:* No new billing or account action; matches the literal wording of the #1644/#1653 acceptances and #1777's own sequencing; availability tied to the maintainer's machine; two migrations (tunnel → hosted) before Stage 2.
- **B.** Go to Render now, deployed manually from a pinned image digest with `autoDeploy` disabled (render.yaml:100 currently `true`), and explicitly waive #1504 for a private non-production instance. — *Consequence:* Reverses q-1; moves the instance outside 'self-hosted... tunnel-fronted HTTPS', so #1644 and #1653 must both be re-recorded; requires editing render.yaml:100 and recording the waiver of #1777's declared #1504 dependency; ~USD 7/month plus disk; and it does not by itself lift the solo-sprint sequencing gate that actually blocks #1772.
- **C.** Render, but only after #1504 is satisfied (branch/tag policy confirmed, non-deploying reviewer-gate rehearsal recorded). — *Consequence:* Satisfies programme:563 and #1777's dependency honestly, but adds an open, unmilestoned issue to the critical path of a proof that is already sequencing-blocked.
- **D.** Railway after the bounded comparison programme:578-586 requires. — *Consequence:* Comparison work first; programme:588 warns against churn for headline price; railway.toml is present but less exercised than render.yaml.

*Recommendation:* **A** — Recommending Render because 'the tunnel stage has produced nothing since 2026-08-19' would misread the record: #1772's own 2026-08-23 comment attributes the absence to the solo sprint and the q-8 sequencing ruling, and states 2026-09-01 is 'a floor for that assessment, not an eligibility date'. No host choice removes that. A also keeps the deployment inside the exact wording the #1644 and #1653 risk acceptances were granted for, and matches #1777's own disposition. PRODUCT_DIRECTION.md:105 puts the collaboration proof as beginning at v0.3 and completing post-launch, so the v0.3 RC date does not force a hosted instance. Choosing B is defensible if the maintainer wants always-on availability now — but it is a reversal of q-1 and must be recorded as one, together with re-recorded #1644/#1653 scope, `autoDeploy: false`, and an explicit #1504 waiver. Rejected without an option row: Azure Student credit as the host — OUTSTANDING_TASKS.md:22 (BEN-1) and RELEASE_TRUST_AND_DISTRIBUTION.md:84-86 confine benefits to disposable lab/staging work, never permanent architecture.

*Reversibility:* Easy on data (one SQLite file plus one key; backup.sh plus the restore drill is the exit path). Harder on record-keeping: option B needs q-1 superseded in writing and two risk acceptances re-scoped before it is legitimate.

*Downstream:* #1777, #1772, #1504 (OPEN, no milestone — an unmet precondition of options B/C, not merely downstream), #1992

#### ADR-0061.5 `budget-alerts-cost-owner` — What monthly budget, alert threshold, and named cost owner apply to infrastructure plus LLM spend?

*Why it matters.* #1772's Stage 1 boundary requires a 'named infrastructure-cost owner and named LLM payer, with provider/egress disclosure'. Without a ceiling and a breach action, a shared instance with live triage has no spend bound and no rehearsed response.

*Evidence.*

- #1772 body, Stage 1 boundary: 'named infrastructure-cost owner and named LLM payer, with provider/egress disclosure'
- programme:675-682 — monthly budget ceiling; provider spending alerts where available; disk and egress monitoring; incident contact and shutdown procedure; export/decommission procedure
- RELEASE_TRUST_AND_DISTRIBUTION.md:90 'These are planning inputs, not purchases or quotes. Re-verify them immediately before an owner decision'; :98 Render ~USD 7/month plus storage/egress (self-host + tunnel option A costs nothing new beyond the tunnel provider's free/paid tier)
- docs/security/MANAGED_KEY_USAGE_POLICY.md:23-27 — RequestsPerHour 60, TokensPerDay 100,000, and `LlmQuota:GlobalBudgetCeilingTokens` 'Unlimited (operator-set)'
- #237 is CLOSED (2026-03-28, 'SEC-17: Managed-key quota, budget, and kill-switch guardrails for LLM usage') — the guardrails are shipped; it is not a live tracking target for this decision
- docs/ops/BUDGET_BREACH_RUNBOOK.md and docs/ops/CLOUD_COST_OBSERVABILITY.md exist under docs/ops/

*Options.*

- **A.** Maintainer is sole cost owner and sole LLM payer; a single all-in monthly ceiling (host + provider) recorded on #1772; provider spend alert plus `LlmQuota:GlobalBudgetCeilingTokens` set to a real number; breach action = disable live providers, not shut the instance down. — *Consequence:* One accountable person, collaborator pays nothing, and the breach action reuses the shipped kill-switch/quota chain so the collaboration walkthroughs survive a spend stop. Amounts must be re-verified at purchase (RELEASE_TRUST_AND_DISTRIBUTION.md:90).
- **B.** Split cost with the collaborator. — *Consequence:* Introduces a money relationship into a dogfooding proof and complicates the #2012-neutral 'no commercial commitment' framing; needs a disclosure the two-person scope does not otherwise require.

*Recommendation:* **A** — A is what #1772 asks for in the fewest moving parts, and it is the only option that keeps a private proof free of commercial entanglement. Set `LlmQuota:GlobalBudgetCeilingTokens` explicitly — it is Unlimited by default (MANAGED_KEY_USAGE_POLICY.md:27), so an unset ceiling is the failure mode. Record the numbers on #1772 without account details, per CL-1. Rejected without an option row: funding from Azure Student/GitHub benefits — OUTSTANDING_TASKS.md:22 (BEN-1) restricts benefits to disposable lab work with expiry and billing-transition risk.

*Reversibility:* Trivial — numbers in an issue comment plus two provider settings.

*Downstream:* #1772, #1777

#### ADR-0061.6 `llm-cost-ownership` — Who pays for LLM calls on the shared instance, and how is egress disclosed to the collaborator?

*Why it matters.* Only a deployment-global provider key exists, so any live triage on the shared box bills the key owner and sends the collaborator's content to OpenAI under that account. ADR:37 requires 'BYO or explicitly operator-funded LLM credentials with cost and egress disclosure' — BYO is not buildable, so the choice is operator-funded or live providers off.

*Evidence.*

- ADR-0061:37 — 'BYO or explicitly operator-funded LLM credentials with cost and egress disclosure'
- docs/security/MANAGED_KEY_USAGE_POLICY.md:17 — 'Provider configuration is currently deployment-global: users cannot add or manage a per-user provider key in Taskdeck, and **Settings -> API Keys** manages MCP `tdsk_` credentials only. A future BYOK experience requires a separate design decision'; #1879 (OPEN) is that decision
- deploy/render.yaml:59-62 ships `Llm__EnableLiveProviders: "false"` and `Llm__Provider: Mock` by default, with the OpenAI key lines commented out at :63-64 — so live triage on a hosted instance is an explicit opt-in, not the default
- EgressDisclosureController.cs:8-25 — authenticated `GET /api/privacy/egress` returning the egress registry
- #1325 AC2 — the privacy note must say plainly 'maintainer's key = maintainer pays + content egresses under maintainer's provider account' and echo the egress-disclosure surface
- docs/security/MANAGED_KEY_USAGE_POLICY.md:23-27 shipped per-user request/token limits and the operator-set global ceiling

*Options.*

- **A.** Operator-funded: maintainer's provider key as the deployment-global key, live providers explicitly enabled, quota and global ceiling set, and a written disclosure to the collaborator pointing at /api/privacy/egress and the managed-key policy. — *Consequence:* Works with shipped code; the transcript loop the v0.2 milestone just shipped is exercised in the collaboration proof; maintainer bears cost and the collaborator's content egresses under the maintainer's provider account, which must be disclosed before the collaborator captures anything real.
- **B.** Live LLM providers off on the shared instance (`Llm__EnableLiveProviders=false`, Mock/deterministic extractor) — the shipped render.yaml default. — *Consequence:* Zero LLM cost and zero LLM-provider egress, and zero LLM-provider egress; but the collaboration proof exercises none of the LLM transcript triage, so the instance proves sharing and realtime only. This yields zero LLM-provider egress only: connectors, outbound webhooks, Sentry and analytics remain independent egress destinations (EgressRegistry.GetSeedEntries, lines 177-203) and the general egress disclosure still applies.

*Recommendation:* **A** — ADR:37's 'BYO where practical' is not practical — no per-user key surface exists and #1879 is still open — so operator-funded is the only variant of the ADR's own text that is buildable today. A reuses shipped controls (managed-key policy, per-user quotas, global ceiling, egress endpoint) and keeps the loop live-verified. Note that A requires flipping the render.yaml defaults at :59-62; B is the fallback if the collaborator does not consent to third-party egress, and it is the safer default until the disclosure has actually been given. Rejected without an option row: per-user BYO keys — not buildable until #1879's decision ships, so choosing it blocks the proof on new product surface.

*Reversibility:* Easy — a config flag either way; swapping to BYOK later is a separate ADR (#1879).

*Downstream:* #1772, #1879, #1325

#### ADR-0061.7 `backup-retention-destination` — How often is the SQLite volume backed up, how long are copies kept, and where do they live?

*Why it matters.* ADR:34 and #1772 require an application-consistent encrypted SQLite backup; a copy that lives only on the instance's own disk dies with the host or the account.

*Evidence.*

- ADR-0061:34 'backup of both SQLite and the connector-encryption key'; #1772 Stage 1: 'an application-consistent encrypted SQLite backup, with the connector-encryption key backed up separately'
- STATUS.md:132-133 — pre-migration snapshot via SQLite's online backup API inside the cross-process migration lock, WAL-checkpointed so each snapshot is a standalone single file; STATUS.md:135 defaults `Database:Backup:Enabled=true`, `RetainCount=5`, directory next to the DB. This is a *pre-migration* trigger: 'Nothing is copied on an ordinary boot with an up-to-date schema'
- scripts/backup.sh — `--retain N` retention (:18, pruning at :192-195); integrity check at :160-168
- docs/ops/DISASTER_RECOVERY_RUNBOOK.md RPO/rotation defaults; programme:655-665 (application-consistent backup, encrypted off-platform target, key preserved separately, daily schedule or approved RPO, retention/deletion policy, integrity checks, 'provider snapshots treated as a supplement, not the only recovery mechanism'), :669-670 suggested RPO ≤24h and RTO ≤4h
- programme:564 — 'do not equate provider disk snapshots with an application-consistent database recovery plan'

*Options.*

- **A.** Scheduled daily `backup.sh` run on the instance (retain 7) plus a weekly encrypted copy to maintainer-controlled off-platform storage with a stated retention window; DB copies stored separately from the connector key. — *Consequence:* Meets programme:669's RPO ≤24h and survives host or account loss; requires one scheduled job and one encrypted destination the maintainer controls. NOTE (review finding): deploy/Dockerfile.production copies neither scripts/backup.sh nor a sqlite3 binary into the runtime image (lines 104-132), so 'a scheduled job on the instance' needs either the tooling added to the image, a sidecar, or a host-volume procedure — that gap is #1777 scope and must be closed before A is executable. RPO honesty: the daily on-instance copies die with the instance/volume/account, so for HOST loss the RPO is the age of the last off-platform copy — run the encrypted off-platform transfer after every daily backup, or record an accepted weekly disaster-loss window.
- **B.** Provider disk snapshots only. — *Consequence:* Directly contradicted by programme:564 and :665; ties recovery to the host account and leaves 'application-consistent' unproven.

*Recommendation:* **A** — A reuses the shipped WAL-safe backup path and the DR runbook defaults, and the off-platform copy is the only thing that makes the restore drill meaningful for host loss. Keep the encrypted DB copies and the connector key in different custody locations — see the next sub-decision — because a single bundle defeats the encryption. Rejected without an option row: relying on the shipped pre-migration auto-backup alone — it fires only when migrations are pending (STATUS.md:134), so the data-loss window equals the time since the last upgrade, which fails ADR:34 for a shared instance.

*Reversibility:* Easy — cadence and destination are operational settings, changeable without touching the app.

*Downstream:* #1772, #1777, #1166

#### ADR-0061.8 `connector-key-custody` — Where is `Connectors:EncryptionKey` held and recovered from, separately from the database backups?

*Why it matters.* Without the key, every stored connector credential is unreadable after a restore; storing it in the same bundle as the DB defeats the encryption. ADR:34 and #1772 both require it backed up separately, and the restore drill must prove decryptability.

*Evidence.*

- ADR-0061:34; #1772 Stage 1 'with the connector-encryption key backed up separately' and 'one restore drill into a clean target, including connector decryptability'
- deploy/render.yaml:30-31 — `Connectors__EncryptionKey` with `sync: false`, i.e. a dashboard-only secret env var; nothing writes it to /app/data on that path
- scripts/backup.sh:175-190 — the key is copied ONLY when a `connector-encryption.key` file sits beside the DB (the comment names the AWS single-node Terraform module, which writes it next to taskdeck.db). :181-182: 'Desktop installs keep the key in appsettings.local.json instead -- back that up separately; it is not a sibling of the DB so nothing is copied here.' So neither the Render nor the desktop path produces a bundled key by default
- FirstRunBootstrapper.cs:696-712 `TryReadPersistedConnectorKey(path, ...)` reads `Connectors:EncryptionKey` from a config file; :737-747 recovery reads it from `localConfigPath` — `appsettings.local.json` (LocalConfigFileName, FirstRunBootstrapper.cs:30) — when a higher-priority source is empty. The recovery source is that file, not the data volume
- OUTSTANDING_TASKS.md:105 records a prior public-dev-key exposure window closed as accepted risk — history argues for deliberate custody rather than a default

*Options.*

- **A.** Key generated offline, held in the maintainer's password manager plus one offline copy; injected only as the host/environment secret; never written beside the DB and never inside a DB backup bundle. — *Consequence:* Two independent custody locations; the restore drill reads the key from the manager, which is exactly what the drill is supposed to prove; no change to backup.sh behaviour is needed because nothing sits beside the DB to copy.
- **B.** Host/dashboard secret only, no second copy (the literal render.yaml:30-31 posture). — *Consequence:* Loss of the host account or the dashboard secret permanently loses every stored connector credential; the ADR's 'backed up separately' is unsatisfied because there is no backup at all.
- **C.** Self-host path: let the app persist the key into `appsettings.local.json` and back that file up separately from the DB backups (backup.sh:181-182 assumes exactly this for desktop installs). — *Consequence:* Matches the shipped self-host/desktop behaviour and FirstRunBootstrapper's recovery path, but the custody is a file on the same machine as the DB unless the separate backup actually leaves that machine; needs a written step, not an assumption.

*Recommendation:* **A** — Only A satisfies ADR:34's 'separately' and makes the restore drill prove something, while using the shipped env-var path. If host option A (self-host + tunnel) is chosen, C is the shipped-default behaviour that will happen unless the key is supplied as an env var — so state explicitly which of A or C is in force, and if C, name where the separate `appsettings.local.json` copy is kept. Agents must never read or print the key value.

*Reversibility:* Moderate — custody location is easy to change, but rotating the key is not: no rotation tool ships, and re-encrypting stored connector credentials is unsolved (the same gap #1653 records for MFA secrets).

*Downstream:* #1772, #1777, #1131, #1653

#### ADR-0061.9 `restore-target` — What is the 'clean target' for the one required restore drill?

*Why it matters.* ADR:35 and #1772 require one real restore into a clean target proving connector decryptability. The target choice sets what the drill actually proves and whether it costs an account action (CL-1 forbids creating or billing an account until authorized).

*Evidence.*

- ADR-0061:35 'one real restore drill'; #1772 Stage 1 'one restore drill into a clean target, including connector decryptability'
- #1777 bounded implementation: 'Restore into a clean target and prove connector decryptability'
- programme:661-663 — 'one clean restore into a fresh service/volume'; 'restored users, boards, connector decryption, and application startup verified'; :670 RTO ≤4h for a maintainer-run restore
- deploy/Dockerfile.production is the single combined SPA+API container used by both deploy configs; the published container image is public (OUTSTANDING_TASKS.md:160)
- OUTSTANDING_TASKS.md:21 CL-1 — 'Do not create or bill an account until authorized'

*Options.*

- **A.** Fresh local container from the exact image digest, restored DB file plus the key from custody; verify login, the shared board, connector decryption, and the health/version endpoint; record measured RTO. — *Consequence:* No account action and no billing; proves the backup + separate-key path end to end; does not prove host-side disk or volume mechanics. NOTE (review finding): 'connector decryptability' cannot be verified by login/board/health checks — no production call site decrypts stored credentials (ConnectorCredentialService.GetCredentialAsync only maps ciphertext presence to HasCredential, ConnectorCredentialService.cs:83-125), so a wrong key passes every listed check. The drill needs a non-secret-exposing decrypt verification seam (e.g. a maintainer-only health probe that round-trips one stored credential) added first — a prerequisite work item for #1777.
- **B.** A second temporary service and volume on the chosen host, deleted after the drill. — *Consequence:* Proves the full hosted restore path including volume mechanics, at a small extra cost and an account action that CL-1 requires authorization for; only meaningful if a hosted option is chosen in host-selection.

*Recommendation:* **A** — A is the cheapest drill that proves what ADR:35 asks — an application-consistent backup plus a separately held key reconstitute a working instance with decryptable connector credentials — and it needs no account action, which keeps it inside CL-1. B is the natural Stage 2 upgrade and belongs with #1777's rollback drill if a hosted migration ever runs. Rejected without an option row: restoring in place onto the live volume — it is not a 'clean' target, risks the live instance, and proves almost nothing. Record the decrypt-verification seam as a prerequisite; without it the drill proves restore, not decryptability.

*Reversibility:* Trivial — a drill, not a commitment; it can be repeated on a different target later.

*Downstream:* #1772, #1777

#### ADR-0061.10 `operating-model` — Is the v0.3 proof maintainer-operated trusted self-hosting, or the start of a managed service? (Confirm the ADR default — no new work either way.)

*Why it matters.* Sets whether the instance is a dogfooding proof (P8) or a product commitment carrying tenancy, billing, and support expectations the codebase does not have. ADR:61-70 lists this as decision 1 and already proposes an answer.

*Evidence.*

- ADR-0061:65 (decision 1) and :69-70 — 'The proposed default is maintainer-operated trusted self-hosting, BYO provider keys where practical, and no managed-service commitment before #2012 and retention evidence'
- ADR-0061:50-55 — managed SaaS 'a separate later product and operating model' requiring tenancy, PostgreSQL, billing, account recovery, abuse controls, observability, legal ops, incident response, DR, support; 'Approval of a trusted instance does not approve this milestone'
- docs/strategy/PRODUCT_DIRECTION.md:92-93 (P8: 'a trusted small shared instance with real principals before speculative multi-tenancy'); :105 'Small-team collaboration proof (trusted shared instance, macOS golden path) begins here **and completes post-launch**'; :107 hosted/commercial exploration 'only after retention evidence'
- #1772 body: 'It is not a managed-service or public-SaaS plan'
- docs/REVIVAL_PLAN.md:31 — 'Candidate paid surfaces (not built yet...): managed hosted instance, ... team workspaces at scale'; :35 records the intent that a hosted instance is the first paid product only after 3-6 months of retention measurement

*Options.*

- **A.** Maintainer-operated trusted self-hosting; evidence always says 'private shared instance', never SaaS, multi-tenant, or production-ready (the ADR default at :69-70, and #1772's own AC). — *Consequence:* No tenancy, billing, or support work; #1772 stays a proof; managed hosting stays 'Later'.
- **B.** Treat the instance as a managed-service pilot, with the collaborator as first hosted customer. — *Consequence:* Pulls in the entire ADR:52-54 list (tenancy, billing, recovery, abuse controls, support) and collides with #2012 and the retention-first sequencing in REVIVAL_PLAN.md:35.
- **C.** Defer the shared-instance proof past v0.3 entirely. — *Consequence:* PRODUCT_DIRECTION.md:105 has the proof beginning at v0.3 and completing post-launch, so deferral is a direction change, not a schedule slip; #1772/#1777 park again with no collaboration evidence.

*Recommendation:* **A** — A restates the ADR's own default and #1772's framing, and it is the only option supported by what is built: REVIVAL_PLAN.md:31 lists 'managed hosted instance' and 'team workspaces at scale' as candidate paid surfaces explicitly 'not built yet', so B would be a claim without substance. C is a live option only if the maintainer wants the collaboration proof off the v0.3 horizon entirely — note that PRODUCT_DIRECTION.md:105 already allows it to complete post-launch, so C is less necessary than it looks.

*Reversibility:* Easy — a later accepted ADR can promote the instance to Stage 2 or 3; nothing in A forecloses it (ADR:55 makes the promotion an explicit separate approval).

*Downstream:* #1772, #1777, #1325

#### ADR-0061.11 `2012-blocks-managed-path` — Does the open commercial/licensing decision (#2012) block any future public managed-service path? (Confirm the ADR default — no new work either way.)

*Why it matters.* Determines whether managed hosting can be planned before the business model is chosen, and what ADR Stage 3's gate actually is. OUTSTANDING_TASKS.md:14 names this question explicitly.

*Evidence.*

- ADR-0061:67 (decision 3) and :69-70 default — 'no managed-service commitment before `#2012` and retention evidence'
- #1772 AC: '#2012 is resolved before any public managed-service commitment'
- #2012 stop criterion (body): 'Closes when the maintainer's model decision is recorded (ADR or dated ruling) **and the contribution-policy question is answered**'; its human/legal-only list has three items — select the business model; decide whether a CLA or contribution-policy change is needed before accepting further external core contributions; if a transition is chosen, record the boundary in a new ADR superseding/amending ADR-0050
- #2012 audit comment 2026-08-24, follow-up 2: 'Decide the inbound-rights instrument before ever reopening contributions — a CLA or exclusive-license grant if relicensing flexibility must be preserved; DCO alone does not preserve it.' The audit itself found zero external human contributions ever (0 non-maintainer PRs, any state)
- programme:686-697 — do not seed public managed-SaaS implementation until an accepted decision covers tenancy, data architecture, billing, recovery, abuse controls, legal ops, observability, DR/support, 'commercial/licensing decision from #2012', and 'evidence that private/shared use has retention worth scaling'
- docs/REVIVAL_PLAN.md:32 (control plane stays private) and :35 (retention-first monetization sequencing); PRODUCT_DIRECTION.md:107 hosted/commercial = Later

*Options.*

- **A.** Yes, hard gate: no public managed-service commitment, pricing, or signup until #2012 is closed AND retention evidence exists (the ADR default, made explicit). — *Consequence:* Stage 3 cannot be scheduled; the private instance and disposable lab experiments stay allowed.
- **B.** No: a managed service is compatible with a GPL core plus a private control plane (REVIVAL_PLAN.md:32), so only retention evidence gates it. — *Consequence:* Permits hosted planning before the licence question, but a later proprietary pivot could invalidate the messaging of a public GPL-hosted offer, and #2012's inbound-rights question would still be unanswered.
- **C.** Partial: #2012 blocks public commitment and publicity, not private/staging experiments or a hosted-instance design spike. — *Consequence:* Practically identical to A for v0.3 while naming the carve-out for spikes; the carve-out already exists in effect via BEN-1's disposable lab wording.

*Recommendation:* **A** — Both the ADR default and #1772's acceptance already say this; ratifying it removes the ambiguity OUTSTANDING_TASKS.md:14 names. Note what A does not mean: #2012 is not one decision away. Its stop criterion is two-part — a recorded model choice AND an answered contribution-policy/inbound-rights question — so the gate does not lift on a model ruling alone.

*Reversibility:* Moderate, not easy — the gate lifts only when both halves of #2012's stop criterion are met (model decision recorded AND contribution-policy/inbound-rights answered), plus the separate retention-evidence condition. A recorded model choice by itself neither closes #2012 nor lifts this gate.

*Downstream:* #2012, #1482, #1777

<details><summary>Sources read for this brief</summary>

- docs/decisions/ADR-0061-trusted-shared-instance-and-managed-saas-boundary.md (full, 88 lines; grep for MFA/TOTP returns nothing)
- docs/decisions/INDEX.md:55-67 (ADR-0051..0063 rows, incl. the ADR-0057:61 and ADR-0059:63 qualified-acceptance precedents and ADR-0060/0061/0062 Proposed rows)
- gh issue view 1772 (body + the 2026-08-23 realignment comment in full: q-1 host ruling, q-8 sequencing ruling, 'Blocked on the solo sprint, not on code')
- gh issue view 1777 (body incl. the 'Depends on' list naming #1504, + the 2026-08-23 'two steps out of order' comment)
- gh issue view 2012 (body incl. the two-part stop criterion and the three human/legal-only items, + the 2026-08-24 copyright/contribution audit comment incl. follow-up 2)
- gh issue view 1325 (body; no user count anywhere), 1504 (OPEN, milestone none, unchecked ACs), 1644 and 1653 (all comments, incl. the 2026-08-19 scoped acceptances and 2026-08-23 realignments)
- gh issue view states: #237 CLOSED 2026-03-28; #1992, #1131, #1166, #1482, #2084, #2091, #1879, #1310, #2010 all OPEN
- OUTSTANDING_TASKS.md:12-23 (Last reviewed 2026-08-27; item 14 ratification batch, 18-20 RT-1..RT-3, 21 CL-1, 22 BEN-1, 23 DIST-1); :105, :160 referenced from the draft
- docs/ops/RELEASE_TRUST_AND_DISTRIBUTION.md:70-100 (Private cloud boundary at :73-82 incl. :77; Azure Student limits :84-86; dated cost envelope :88-99 incl. Render row :98)
- docs/spikes/TASKDECK_RELEASE_TRUST_DISTRIBUTION_AND_CLOUD_PROGRAMME.md:550-600 (8.2 Render recommendation incl. :562 perimeter, :563 protected gate, :566 auto-deploy reconsideration; 8.3 Railway :568-588; 8.4 Heroku) and :655-697 (backup/restore requirements, RPO/RTO targets, cost and operations, 8.7 managed-SaaS gate)
- docs/strategy/PRODUCT_DIRECTION.md:88-112 (P7/P8, release-theme ladder incl. the full v0.3 sentence at :105 and Later at :107)
- docs/REVIVAL_PLAN.md:26-40 (commitments 1-5 incl. :31 candidate paid surfaces 'not built yet' and :32 private control plane; :35 monetization sequencing)
- docs/STATUS.md section reads: :114-118, :130-135 (pre-migration backup), :149-153, :464-468 (BoardsHub at :466), :509-513 (E2E coverage at :511)
- docs/security/MANAGED_KEY_USAGE_POLICY.md:1-30 (deployment-global key at :17; quota table :23-27 incl. GlobalBudgetCeilingTokens at :27)
- scripts/backup.sh:160-195 (integrity check, connector-key copy only when the key is a sibling of the DB, :175-182 comment, retention pruning)
- backend/src/Taskdeck.Application/Services/RegistrationSettings.cs (full, 16 lines; default Open at :15); RegistrationPolicyService.cs:85-110
- backend/src/Taskdeck.Api/FirstRun/FirstRunBootstrapper.cs:30 (LocalConfigFileName appsettings.local.json), :690-712 (TryReadPersistedConnectorKey), :728-750 (recovery from localConfigPath)
- backend/src/Taskdeck.Api/Controllers/EgressDisclosureController.cs:1-30
- deploy/render.yaml (full, 101 lines: key secrets :30-35, registration Closed :52-53, live providers off :59-62, disk :85-88, numInstances :94, plan starter :97, autoDeploy true :100, branch main :101); deploy/railway.toml:1-30 (Closed + first-owner invite instruction at :12-13)
- git: origin/main = 927236bd0; ADR-0061 last touched c07b765c5 (2026-08-26); `git diff --stat origin/main origin/integration/v0.3.0 -- <ADR-0061>` empty (identical on both branches)

</details>

## ADR-0062

### Custom Fields, Aggregates, and Threshold Rules — maintainer decision brief (revised)

**Status line:** docs/decisions/ADR-0062-custom-fields-aggregates-and-threshold-rules.md:3 — "- Status: Proposed" (Date 2026-08-26; Deciders: "Maintainer ratification pending"; Related: #2091, #2094, ADR-0060)

**Context.** Shipped reality on origin/main 927236bd0: a card carries Title, Description, DueDate, IsBlocked/BlockReason, Position and board-scoped labels (Card.cs:32-38, Label.cs:13); there is no CustomFieldDefinition, CustomFieldValue, ThresholdRule or WorkLog type anywhere in backend/src on main OR on integration/v0.3.0 (`git grep -l -E 'CustomFieldDefinition|CustomFieldValue|ThresholdRule|class WorkLog' <ref> -- backend/src` returns nothing for both refs; branch tip ad5325419). Derived metrics already exist read-only from the audit log (BoardMetricsService, ForecastingService — STATUS.md:416,459). ADR-0062 proposes a semantic boundary only, adds no schema (line 88), and leaves four questions open (lines 77-80). It is chained behind ADR-0060 (#2084 OPEN, ADR-0060:3 Status Proposed), is ratified by #2091 (OPEN, 0 comments, whose first acceptance bullet is "ADR-0062 stays Proposed until the maintainer records answers here"), and explicitly gates #2094 ("Blocked on the dedicated ADR-0062 decision issue and accepted ADR-0060 scope"; v0.3 conditional, Priority III). Whether it also gates #2093 is NOT stated by #2093, whose Dependencies name only "accepted ADR-0060 identity terms and existing board-access rules" (v0.3, Priority II) — see sub-decision adr0062-gate-on-2093. Release position: v0.2.0 has NOT shipped — milestone "v0.2 — Coherent Context-to-Action Loop" is 0 open / 15 closed with final target 2026-09-01 (REVIVAL_PLAN.md:47), its RC is origin/main 927236bd0 — while the v0.3 RC target is 2026-09-04 (REVIVAL_PLAN.md:48), so the usable v0.3 build window after the v0.2 tag is roughly 3 days, not 6.

**If ratified unmodified.** Accepting ADR-0062's text as-is commits Taskdeck to seven things and leaves four unanswered.

COMMITMENTS:
- Separate CustomFieldDefinition and typed CustomFieldValue records — never tags, never one polymorphic value+rule table (lines 16-18, 92-98).
- An OPTIONAL type vocabulary at lines 20-29 ("may include": text; number and decimal; duration; date; boolean; single-select and multi-select; participant reference; URL). Because the ADR says "may include", accepting it does not require every listed type in the first slice and does not conflict with #2094's narrower six — choosing the six is an application of the ADR, not a revision of it.
- Formula fields excluded from the first slice; colour is presentation metadata only (line 31).
- Built-in work measures kept distinct from custom fields, with estimate, remaining effort, actual logged, cycle time, lead time, relative size and capacity as seven different values; assignment never implies logged work (lines 33-45).
- Aggregates are derived read models over authoritative values and events, never client-written (line 49).
- Threshold rules may flag or notify, and may only mutate through the existing proposal/review/apply path; no rule approves or mutates a board (line 51).
- Historical effort by actor or stage only from an immutable work-log or lifecycle-event model; current column membership cannot prove past activity (line 65).

CONTRACT GAP: the seven-point per-operation contract at lines 55-63 OMITS account deletion, migration bootstrap proof, and rollback behavior relative to ADR-0060:78-80 — while the implementing issues require rollback (#2091, #2093, #2094) and migration bootstrap (#2094) but none require account deletion. Ratified unmodified, the ADR is weaker than ADR-0060's contract and than parts of the issues that implement it.

WHAT IT STILL DOES NOT DO: it authorises no schema and creates no release commitment (line 88), and it leaves lines 77-80 — timing, ownership scope, time-tracking fit, deletion policy — unanswered. Per #2091's first acceptance bullet, ADR-0062 stays Proposed until those answers are recorded on #2091, so #2094 remains blocked either way.

**Cross-ADR dependencies.**

- ADR-0060 (#2084 OPEN, Status Proposed at ADR-0060:3): must be Accepted before BOTH #2094 and #2093 schema work — #2093's Dependencies name it explicitly. Its decisions 1 (Project first-class before v0.3, ADR-0060:86), 5 (generic custom fields in v0.3 or later, :90) and 6 (actual time tracking fits the thesis, :91) mirror this brief's field-scope-ownership, custom-field-timing and actual-time-tracking-fit — answer them consistently in the same sitting.
- ADR-0060:78-80 (cross-cutting contract): the canonical ten-item bar. ADR-0062:55-63 lists only seven — see sub-decision cross-cutting-contract-amendment.
- ADR-0061 (#1772 OPEN, Status Proposed): supplies the only named, citable deferral gates. Stage 1 "Trusted shared instance" is the v0.3 collaboration proof owned by #1772 (ADR-0061:23-26); stage 2 "Dependable small-team alpha" is later hardening (ADR-0061:42-48). ADR-0062:77's phrase "collaboration alpha" matches neither and appears nowhere else in the repo — the maintainer should replace it with one of these two when recording the timing answer.
- ADR-0003 / GP-06 / ADR-0056 (Accepted, shipped policy): threshold rules are non-human actors and must use the proposal/review/apply path; a rule can never approve or mutate a board directly (ADR-0062:51; ADR-0060:60-61).
- ADR-0057 (Accepted as direction only, 2026-08-24, openness caveat): rule auto-apply is out of scope for ADR-0062 entirely. PRODUCT_DIRECTION.md:50 forbids any standing policy or confidence threshold from auto-applying, and :66-70 requires a separately gated implementation slice (earliest v0.3) before any auto-approval surface is built. Ratifying ADR-0062 authorizes none of it.

| # | Sub-decision | Recommended |
| --- | --- | --- |
| 1 | `custom-field-timing` — When does the generic typed custom-field foundation (#2094) ship relative to ADR-0060 acceptance and the v0.3 release — and what, if anything, ships in v0.3 instead? | **B** |
| 2 | `adr0062-gate-on-2093` — Does ADR-0062 gate #2093, or is #2093 released by ADR-0060 acceptance alone? | **A** |
| 3 | `first-type-vocabulary` — Which value types form the first generic-field slice — #2094's six, or more of the ADR's optional ("may include") vocabulary? | **A** |
| 4 | `field-scope-ownership` — At which boundary is a CustomFieldDefinition owned: board, workspace, or project? (ADR-0062:78) | **A** |
| 5 | `field-management-permission-role` — Which board role may create, edit, or retire a field DEFINITION, versus which may write field VALUES? | **A** |
| 6 | `actual-time-tracking-fit` — Does an immutable WorkLog (actual time / activity tracking) belong to Taskdeck's context-to-action thesis, and how are estimate and actual kept apart? (ADR-0062:79) | **A** |
| 7 | `aggregate-rollup-semantics` — What is an aggregate/roll-up: a derived read model computed from authoritative values, a stored/cached value, or a writable summary field? | **A** |
| 8 | `threshold-rule-semantics` — What may a threshold rule do when it fires: flag/notify only, or also emit a proposal through the existing review path? | **B** |
| 9 | `definition-deletion-policy` — What happens to stored values, audit history and exports when a field definition is deleted? (ADR-0062:80) | **A** |
| 10 | `cross-cutting-contract-amendment` — Should ADR-0062's seven-point cross-cutting contract (lines 55-63) be amended to add account deletion, migration bootstrap proof, and rollback behavior, matching its siblings? | **A** |

#### ADR-0062.1 `custom-field-timing` — When does the generic typed custom-field foundation (#2094) ship relative to ADR-0060 acceptance and the v0.3 release — and what, if anything, ships in v0.3 instead?

*Why it matters.* v0.2.0 has not shipped (milestone 0 open / 15 closed, final target 2026-09-01, RC at origin/main 927236bd0), and the v0.3 RC target is 2026-09-04 — so the usable v0.3 build window is ~3 days, not 6. ADR-0060 is still Proposed with #2084 OPEN, which blocks BOTH #2094 and #2093. #2094 needs a new persisted model plus migration, proposal diff/apply, MCP/API, export/import/deletion, realtime and rollback coverage; admitting it now risks v0.3 launch scope (P10, PRODUCT_DIRECTION.md:96). Note the deferral target in ADR-0062:77 — "after the collaboration alpha" — is undefined: that phrase occurs nowhere else in the repo (grep over *.md returns only ADR-0062:77 plus two .worktrees copies). ADR-0061 names two candidate gates instead: stage 1 "Trusted shared instance" = the v0.3 collaboration proof owned by #1772 (ADR-0061:23-26), and stage 2 "Dependable small-team alpha" (ADR-0061:42-48). Options B and C below both use stage 2; picking stage 1 instead would make B barely a deferral at all, since #1772 is itself on the v0.3 milestone.

*Evidence.*

- ADR-0062:69 ("Record the ADR-0062 decisions in #2091 and decide whether generic fields belong in v0.3 or later"); ADR-0062:70 ("If needed, ship only narrowly defined built-in estimates before a generic field system") — conditional sequencing, not a prescription
- ADR-0062:77 ("Do generic custom fields belong in v0.3 or after the collaboration alpha?"); the phrase "collaboration alpha" appears nowhere else in the repo
- ADR-0061:23-26 (stage 1 trusted shared instance = v0.3 collaboration proof owned by #1772) vs ADR-0061:42-48 (stage 2 dependable small-team alpha)
- ADR-0060:69-71 (built-in estimates as independent migrations only when their issues are admitted); ADR-0060:90 (maintainer decision 5: whether generic custom fields enter v0.3 or later); ADR-0060:3 Status Proposed, #2084 OPEN
- docs/REVIVAL_PLAN.md:47 (v0.2 final target 2026-09-01), :48 (v0.3 RC 2026-09-04), :58 (waiver-seeded v0.3 candidates #2093, #2094)
- gh api milestones: "v0.2 — Coherent Context-to-Action Loop" open_issues 0 / closed_issues 15, due 2026-09-01; "v0.3" 20 open, due 2026-09-09
- issue #2094 body: "Blocked on the dedicated ADR-0062 decision issue and accepted ADR-0060 scope. v0.3 conditional, Priority III."
- issue #2093 body Scope: Principal/Participant boundary + multiple assignments per work item + one built-in estimate + current-state roll-ups by participant and board/column + acting-principal attribution with display-only persona; Dependencies: "Depends on accepted ADR-0060 identity terms and existing board-access rules. v0.3, Priority II."
- issue #2093 acceptance: "Audit, export/import/deletion, MCP/API, realtime, concurrency, migration, and rollback tests pass."
- docs/strategy/PRODUCT_DIRECTION.md:96 (P10 broad vision does not admit broad scope)
- `git grep -l -E 'CustomFieldDefinition|CustomFieldValue|ThresholdRule|class WorkLog' origin/integration/v0.3.0 -- backend/src` → no matches (tip ad5325419); same grep on origin/main → no matches

*Options.*

- **A.** Admit #2094 (generic typed custom fields) into v0.3 as soon as ADR-0060 is Accepted. — *Consequence:* Adds a new persisted model (definition + typed value), an EF/SQLite migration, proposal diff/apply ops, MCP/API and export/import/deletion round-trip inside a ~3-day post-v0.2 build window; highest launch risk, and conditional on ADR-0060 acceptance (#2084 OPEN).
- **B.** Ship #2093 in v0.3 — Principal/Participant boundary, multiple assignments, one built-in estimate, current-state roll-ups by participant and board/column, acting-principal attribution — once ADR-0060 is Accepted; defer generic fields (#2094) to after ADR-0061 stage 2 "Dependable small-team alpha" (ADR-0061:42-48). — *Consequence:* Matches ADR-0062:70 sequencing and ADR-0060 stage 3, and gives dogfooding an estimate without a generic type system or formulas. But this is ALSO a new identity/assignment schema and migration inside the same RC window, with #2093's own acceptance demanding audit, export/import/deletion, MCP/API, realtime, concurrency, migration and rollback tests — lower risk than a generic field system, not zero. Conditional on ADR-0060 acceptance (#2084 OPEN). #2094 is re-milestoned off v0.3.
- **C.** Defer both #2093 and #2094 to after ADR-0061 stage 2 "Dependable small-team alpha" (ADR-0061:42-48); v0.3 adds no field or assignment schema. — *Consequence:* Zero v0.3 schema risk and no dependency on ADR-0060 landing before the RC. The dogfooding "estimates/deadlines/time measures" need (ADR-0062:10) stays unmet and #2093 loses its v0.3 Priority II slot.
- **D.** Reject generic fields as a direction; grow built-in typed fields only (estimate, stage, etc.), each by its own ADR + migration. — *Consequence:* Revises ADR-0062's field-definition/value core (lines 16-31) rather than ratifying it. Simpler permissions, audit and export per field; every new field becomes a migration and a decision, so adaptation to real workflows is slow. #2094 closes as won't-do.

*Recommendation:* **B** — ADR-0062:70 conditionally sequences narrowly defined built-in estimates before a generic field system ("If needed"), ADR-0060:69-71 keeps built-in estimates as an independent migration, and #2093 already holds the v0.3 Priority II slot while #2094 is explicitly conditional Priority III. A generic field foundation entering a ~3-day post-v0.2 window contradicts P10, and the alpha will show which field types real users actually need. Two conditions must be stated when recording B: it does not start until ADR-0060 is Accepted (#2084), and #2093 is itself a migration + identity surface, not a single nullable column — if the ~3-day window cannot absorb that, C is the honest fallback.

*Reversibility:* Easy — timing only. #2094 stays open and can be re-milestoned once ADR-0060 is Accepted and v0.3 has shipped; nothing is persisted by this choice.

*Downstream:* #2094, #2093, #2084, #2091, #1772

#### ADR-0062.2 `adr0062-gate-on-2093` — Does ADR-0062 gate #2093, or is #2093 released by ADR-0060 acceptance alone?

*Why it matters.* The two issues disagree. #2094 names ADR-0062 as a blocker; #2093 does not mention ADR-0062 at all. Yet #2093 ships a built-in estimate and roll-ups, which are exactly what ADR-0062:33-45 (built-in work measures) and :49 (aggregates are derived read models) constrain. Left implicit, #2093 gets blocked or unblocked by accident: an agent reading #2091's "ADR-0062 stays Proposed until the maintainer records answers here" could park a Priority II v0.3 issue that the maintainer never intended to gate.

*Evidence.*

- issue #2093 "Dependencies and release": "Depends on accepted ADR-0060 identity terms and existing board-access rules." — no ADR-0062 reference
- issue #2094 "Dependencies and release": "Blocked on the dedicated ADR-0062 decision issue and accepted ADR-0060 scope."
- ADR-0062:33-45 (built-in work measures kept distinct; "Assignment does not imply work logged") and :49 (aggregates are derived read models, not client-written values) — both bear directly on #2093's estimate and roll-ups
- issue #2091 acceptance bullet 1: "ADR-0062 stays Proposed until the maintainer records answers here."
- ADR-0062:6 Related: #2091, #2094, ADR-0060 — #2093 is not listed

*Options.*

- **A.** ADR-0062 does gate #2093's estimate/roll-up half: record on #2091 that #2093 may not start until both ADR-0060 is Accepted and the built-in-measure boundary (ADR-0062:33-45) and derived-aggregate rule (ADR-0062:49) are ratified. — *Consequence:* Two ADRs must be settled in the same sitting before the v0.3 Priority II work can start; in exchange the estimate cannot ship as a mutable "actual hours" number and roll-ups cannot ship as stored writable summaries.
- **B.** ADR-0062 does not gate #2093: ADR-0060 acceptance alone releases it, and ADR-0062:33-45/:49 apply as non-blocking design constraints already restated in #2093's own acceptance. — *Consequence:* #2093 can start the moment #2084 is decided. Risk: the estimate/roll-up semantics rest on #2093's acceptance wording ("Roll-ups state that they are assignment/estimate totals, not historical activity or capacity") rather than a ratified ADR; a later ADR-0062 revision could contradict shipped behaviour.
- **C.** Amend the issues rather than rule: add ADR-0062 to #2093's Dependencies and ADR-0062:6's Related list, then answer as A. — *Consequence:* Removes the ambiguity in the record itself, at the cost of two issue/ADR edits; identical practical effect to A.

*Recommendation:* **A** — #2093 ships the first values ADR-0062:33-45 and :49 exist to constrain — a built-in estimate and roll-ups — so ratifying the boundary before the schema is written is cheap and it is the whole point of ADR-0062:69 ("Record the ADR-0062 decisions in #2091"). Both ADRs are already on the same decision desk this sitting, so the extra gate costs nothing in calendar time. If the maintainer prefers not to widen #2093's blockers, C records the same ruling but repairs the issue text too.

*Reversibility:* Easy — this is a recorded interpretation on #2091, changeable before any code exists.

*Downstream:* #2093, #2091, #2084

#### ADR-0062.3 `first-type-vocabulary` — Which value types form the first generic-field slice — #2094's six, or more of the ADR's optional ("may include") vocabulary?

*Why it matters.* ADR-0062:20-29 lists text; number and decimal; duration; date; boolean; single-select and multi-select; participant reference; URL. #2094 narrows to six and explicitly excludes multi-select, duration and participant fields. Duration overlaps the built-in estimate measure (ADR-0062:37) and participant reference overlaps ADR-0060's Assignment (ADR-0060:53-54), so the choice matters — but because ADR-0062:20 says the vocabulary "may include" these types, picking six is an application of the ADR's optional list, not a revision of its text.

*Evidence.*

- ADR-0062:20-29 (type vocabulary, listed as "may include"), :31 (formulas excluded from the first slice; colour is presentation metadata)
- issue #2094 Scope: "Start with text, number, date, boolean, single-select, and URL only"; Non-goals: "No formulas, multi-select, duration, participant fields, aggregates, thresholds, notifications, work logs, or board-as-view migration."
- ADR-0062:37 (estimated effort as a built-in measure), :35 (built-ins are for "shared semantics and indexing"), :65 (a current mutable value cannot prove past activity)
- ADR-0060:53-54 (Assignment links a participant to a work item; responsibility, not completed work)
- issue #2091 decision request: "Ratify or revise ADR-0062 without widening ADR-0060 or #2084."

*Options.*

- **A.** Record #2094's six (text, number, date, boolean, single-select, URL) as the first slice; duration stays only in the built-in estimate, participant references only in Assignment. No ADR text change — the ADR's list is optional ("may include"). — *Consequence:* Smallest typed-value surface; no overlap with the built-in estimate or with ADR-0060 identity concepts; multi-select/duration/participant become later additive types.
- **B.** Record all nine listed types as the first slice. — *Consequence:* One migration covers everything and the ADR needs no edit, but duration duplicates the built-in estimate (ADR-0062:37) and participant reference duplicates Assignment (ADR-0060:53-54); multi-select also complicates proposal diffs and export round-trips. #2094's Scope and Non-goals must be rewritten to match.
- **C.** Six plus duration, so custom fields can express time measures without a new built-in. — *Consequence:* ADR-faithful on the type list (duration is at ADR-0062:24) but lets a mutable "actual hours" number stand in for recorded activity, which ADR-0062:65 says a current value cannot prove, and it duplicates the built-in estimate's shared-semantics role (ADR-0062:35-37). #2094's Non-goals must be amended.

*Recommendation:* **A** — Keeps ADR-0062's own separation intact where it matters: shared-semantics measures (estimate) stay built-in for indexing, identity references stay in Assignment, and generic fields carry only user-defined typed values. It also matches the already-seeded #2094 scope, so no issue text needs rewriting. No ADR text change is needed: the list is explicitly optional, so recording six as the first slice is consistent with ratifying the ADR as written.

*Reversibility:* Easy — additive types can be appended to the vocabulary in later migrations without changing stored values or the definition/value split.

*Downstream:* #2094, #2093

#### ADR-0062.4 `field-scope-ownership` — At which boundary is a CustomFieldDefinition owned: board, workspace, or project? (ADR-0062:78)

*Why it matters.* Only Board is a shipped ownership boundary with access roles. Workspace and Project are ADR-0060 target vocabulary with no entity on main (ADR-0060:13-14). The scope choice fixes permission checks, export shape, and where a definition survives archive — and #2094's first acceptance bullet is "Server validates definition scope, type, constraints, and permissions."

*Evidence.*

- backend/src/Taskdeck.Domain/Entities/Label.cs:13 (labels are BoardId-scoped — the shipped precedent for a board-owned definition)
- backend/src/Taskdeck.Domain/Entities/BoardAccess.cs:61-67 (CanRead/CanWrite/CanManageAccess/CanDelete — the only shipped role model)
- ADR-0060:13-14 ("no durable Workspace, Project, WorkItem, WorkRelation, Actor, Assignment, CustomField, or WorkLog model"); ADR-0060:86 (maintainer decision 1: whether Project becomes first-class before v0.3)
- docs/STATUS.md:42 (GET /api/workspace/collaboration is a server-computed membership contract over boards, not a Workspace entity)
- ADR-0062:78 (open question)

*Options.*

- **A.** Board-scoped definitions now, stored with an explicit scope-kind + scope-id pair so re-parenting to Project/Workspace later is a data migration, not a redesign. — *Consequence:* Reuses the shipped BoardAccess permission model and the label precedent; definitions are duplicated across boards until a Project boundary exists.
- **B.** Workspace-scoped (per-owner) definitions. — *Consequence:* Requires inventing a Workspace boundary before ADR-0060 decides it; there is no shipped role model to reuse for permission checks (the workspace surface today is a computed membership read, STATUS.md:42).
- **C.** Project-scoped definitions. — *Consequence:* Blocked until ADR-0060:86 decision 1 (Project first-class before v0.3?) is ratified; cannot ship in any near slice.
- **D.** User-private definitions (per principal). — *Consequence:* Avoids collaboration questions but breaks shared-instance semantics and makes proposal diffs viewer-dependent — the same card would diff differently for two reviewers.

*Recommendation:* **A** — Board is the only ownership/permission boundary that exists on main, labels already prove the pattern, and the shared-instance write-access hardening applies unchanged (STATUS.md:122). An explicit scope-kind column keeps the ADR-0060 migration path open without pre-empting the Project decision at ADR-0060:86.

*Reversibility:* Moderate — widening the scope later is a data migration with duplicate-merge rules; still cheaper than picking a boundary that does not exist yet.

*Downstream:* #2094, #2084, #2087

#### ADR-0062.5 `field-management-permission-role` — Which board role may create, edit, or retire a field DEFINITION, versus which may write field VALUES?

*Why it matters.* ADR-0062:57 requires "permission checks and server-side validation" for every field operation and #2094's first acceptance bullet requires the server to validate "definition scope, type, constraints, and permissions" — but neither says which role. field-scope-ownership settles only WHERE a definition lives. Definition changes are schema-like and affect every card on the board; value edits are ordinary card writes. If both land on CanWrite, any Editor can retire a definition and hide values across the whole board.

*Evidence.*

- backend/src/Taskdeck.Domain/Entities/BoardAccess.cs:61-67 — CanRead() => true; CanWrite() => Owner|Admin|Editor; CanManageAccess() => Owner|Admin; CanDelete() => Owner
- ADR-0062:57 ("permission checks and server-side validation" per field operation)
- issue #2094 acceptance: "Server validates definition scope, type, constraints, and permissions."
- docs/STATUS.md:122 (#1794 precedent: board-targeted triage was raised from read access to write access — permission level for a board-wide effect is decided deliberately, not inherited)

*Options.*

- **A.** Definition create/edit/retire require the owner-or-Admin level (the AuthorizationService owner short-circuit OR BoardAccess.CanManageAccess); value writes require owner-or-write (owner OR CanWrite); reads owner-or-read. — *Consequence:* Board-wide schema changes need an administrator while day-to-day value edits stay with editors; costs one extra authorization branch and a distinct 403 path in the API and MCP surfaces.
- **B.** Both definition management and value writes at the owner-or-write level. — *Consequence:* Simplest to implement and matches label editing, but any Editor can retire a definition and hide its values across every card on the board — a board-wide effect at a per-card permission level.
- **C.** Definition create/edit/retire owner-only (the CanDelete level); value writes owner-or-write. — *Consequence:* Strictest; an Admin co-owner cannot manage fields, which conflicts with how Admin is used for board administration elsewhere in BoardAccess.

*Recommendation:* **A** — A definition is board-wide structure; a value is card content. The shipped role model already draws exactly this line (CanManageAccess vs CanWrite, BoardAccess.cs:63-65), and STATUS.md:122's #1794 precedent shows this repo raises the required level when an action's blast radius exceeds the surface it is invoked from. Recording this on #2091 closes #2094's first acceptance bullet before implementation starts. Phrase every check through AuthorizationService's owner-or-access predicates (AuthorizationService.cs:96-141), never through BoardAccess methods alone: owners deliberately hold no BoardAccess row, so a row-only rule would lock an owner out of their own board's fields.

*Reversibility:* Easy while nothing is built; loosening A to B later is a one-line policy change, whereas tightening B to A after values exist means revoking a capability users already had.

*Downstream:* #2094, #2091

#### ADR-0062.6 `actual-time-tracking-fit` — Does an immutable WorkLog (actual time / activity tracking) belong to Taskdeck's context-to-action thesis, and how are estimate and actual kept apart? (ADR-0062:79)

*Why it matters.* Dogfooding asked for time measures (ADR-0062:10) but the strategy spine positions Taskdeck as a context-to-action engine, not a PM tool. A mutable "actual hours" number would fake historical activity that only an event log can prove.

*Evidence.*

- ADR-0062:39 ("actual work logged: immutable recorded activity, if work logs are accepted"), :65 ("Historical effort by actor or stage requires an immutable work-log or lifecycle-event model. Current column membership is current state and cannot prove past activity."), :79 (open question)
- ADR-0060:55-56 (WorkLog is an immutable activity record attributed to participant and acting principal), :69-71 ("WorkLog remains a later event-model decision"), :91 (maintainer decision 6: whether actual time tracking fits the product thesis)
- issue #2093 Non-goals: "No WorkLog, time tracking, capacity forecasting, duration formula, auto-scheduling, or generic custom fields."
- docs/STATUS.md:416 (BoardMetricsService derives cycle time creation-to-done from audit log), :459 (ForecastingService uses audit-log card-move events)
- docs/strategy/PRODUCT_DIRECTION.md:27-28 (engine/wedge), :86 (P5 context should become movement)

*Options.*

- **A.** Accept WorkLog conceptually as an immutable, attributed, optional event model; exclude it from v0.3 and from the alpha. Estimate stays a built-in mutable value; "actual" may only ever come from WorkLog or audit-derived lifecycle events. — *Consequence:* No PM weight ships; workload reports keep saying "assignment/estimate totals, not historical activity" (#2093's own acceptance) until event evidence exists; the concept is reserved so nobody stores "actual" as a plain mutable number.
- **B.** Reject time tracking from the thesis outright; remove WorkLog from the ADR-0060/ADR-0062 vocabulary. — *Consequence:* Cleanest scope, but forecloses the agent-run and human activity attribution the accountable-agents theme may want; reversing means re-opening two ADRs.
- **C.** Ship a minimal manual "actual effort" built-in number field in v0.3. — *Consequence:* Contradicts ADR-0062:65 (a mutable current value cannot prove past activity) and #2093's Non-goals; produces roll-ups that read as activity but are not.
- **D.** Derive "actual" solely from lifecycle events (audit-log column moves), never from user-entered logs. — *Consequence:* Zero new schema and already partly shipped (cycle time, STATUS.md:416/459), but cannot express per-person effort; works as the interim source under option A rather than as a rival to it.

*Recommendation:* **A** — Audit-log-derived cycle time is already shipped, which shows the product wants derived time rather than timesheets. Keeping WorkLog as a reserved immutable concept preserves actor attribution for the accountable-agents theme without adding PM weight now, and it explicitly blocks the tempting-but-wrong mutable "actual hours" column that option C would create. Answer ADR-0060:91 decision 6 the same way in the same sitting.

*Reversibility:* Easy while nothing is built; option C would be hard to unwind once values exist in exports and audit rows.

*Downstream:* #2093, #2084, #2091

#### ADR-0062.7 `aggregate-rollup-semantics` — What is an aggregate/roll-up: a derived read model computed from authoritative values, a stored/cached value, or a writable summary field?

*Why it matters.* #2093 ships the first roll-ups (current-state totals by participant and board/column). If they are stored or writable they become a second source of truth that proposals, exports and realtime must reconcile. ADR-0062:49 already settles this in the text, so this sub-decision exists to confirm it — or to revise it deliberately.

*Evidence.*

- ADR-0062:49 ("Aggregates are derived read models or queries over authoritative values and events. They are not custom-field values that clients write directly."), :72 (add aggregate read models after authoritative values and lifecycle events exist)
- issue #2093 Scope: "basic current-state roll-ups by participant and board/column"; acceptance: "Roll-ups state that they are assignment/estimate totals, not historical activity or capacity."
- docs/STATUS.md:416 (BoardMetricsService computes throughput/WIP/cycle time with SQL-level filtering — the shipped derived-on-read precedent)

*Options.*

- **A.** Derived-on-read only (query or read model), never persisted; always labelled as current-state totals. — *Consequence:* Single source of truth, no invalidation bugs; cost is query time, which the existing metrics services already accept at SQLite single-instance scale.
- **B.** Materialised/cached aggregates with realtime invalidation. — *Consequence:* Faster reads, but staleness plus invalidation work on every card/field write; premature for the current single-instance scale and it adds a second thing exports and proposals must reconcile.
- **C.** Writable summary fields that clients may set. NOTE: already rejected by ADR-0062:49 — select only to revise the ADR. — *Consequence:* Would make totals capable of disagreeing with their inputs and proposal diffs ambiguous; requires editing ADR-0062:49.

*Recommendation:* **A** — Confirms the ADR text and the shipped metrics precedent. Caching is an optimisation that can be added behind the same read contract if the alpha shows latency pain, whereas a stored or writable aggregate is a semantic commitment that exports, audit and the proposal diff would inherit permanently.

*Reversibility:* Easy — a cache can be layered under the same API later without changing the contract.

*Downstream:* #2093, #2091

#### ADR-0062.8 `threshold-rule-semantics` — What may a threshold rule do when it fires: flag/notify only, or also emit a proposal through the existing review path?

*Why it matters.* Review-first is ACCEPTED shipped policy, not merely proposed: PRODUCT_DIRECTION.md:49-50 — "Automation-originated board writes are proposal-first ... No standing policy or confidence threshold may auto-apply them" — and :66-70 records that ADR-0057's acceptance ratified direction only, with "no auto-approval surface may be built until an implementation slice is separately gated behind its own issues (earliest v0.3)". Direct auto-apply is therefore OUT OF SCOPE for ADR-0062 and is not offered as a selectable option below; ratifying ADR-0062 cannot authorize it. The remaining live question is whether a rule may originate a proposal at all, which fixes whether a rule needs an actor identity in audit and a new NotificationType.

*Evidence.*

- ADR-0062:51 ("A rule may produce a flag or notification. Any rule that proposes a domain-state mutation must use the existing proposal, review, and apply path. No rule can directly approve or mutate a board."), :73 (add threshold rules only after notification and automation governance is defined)
- docs/strategy/PRODUCT_DIRECTION.md:49-50 (shipped policy, ADR-0003/GP-06), :52 (MCP exposes no approve or apply tool), :66-70 (ADR-0057 direction only; no auto-approval surface until a separately gated slice)
- backend/src/Taskdeck.Domain/Entities/Notification.cs:93-100 (NotificationType = Mention/Assignment/ProposalOutcome/BoardChange/System — no threshold kind exists)
- backend/src/Taskdeck.Application/Services/AutomationProposalService.cs:17-36 (KnownActionVerbs — any rule-originated change must map to these verbs or extend them)
- ADR-0060:60-61 ("No agent or LLM receives a direct mutation path through this model.")

*Options.*

- **A.** Flag + notification only; a rule never creates a proposal or a mutation. — *Consequence:* Safest and smallest: one new NotificationType and no new actor identity. But a "due soon → move to Urgent" wish cannot be expressed at all, even review-first, so rules stay purely informational.
- **B.** Flag/notify, or emit a proposal through the existing proposal/review/apply path; direct apply excluded and left to a separately gated ADR-0057 implementation slice. — *Consequence:* Rules become one more proposal source with full provenance and no new trust class, but they need a rule-principal identity in audit and their operations must map onto the KnownActionVerbs vocabulary (AutomationProposalService.cs:17-36). Implementation still waits on ADR-0062:73 (notification and automation governance), so this ratifies an envelope, not a v0.3 feature.

*Recommendation:* **B** — B is exactly ADR-0062:51 and keeps GP-06 intact: a rule is a non-human actor, so ADR-0056/ADR-0003 already route it through proposals rather than through a new trust class. Ratify the envelope now — it is free. Implementation timing is not decided here: ADR-0062 sequencing step 5 only makes rules follow notification/automation governance (neither a notification kind nor a rule-principal exists yet); a release assignment would be its own sub-decision. Auto-apply is not on this desk: it needs its own gated ADR-0057 slice (PRODUCT_DIRECTION.md:66-70).

*Reversibility:* Easy — envelope only, nothing built. Widening to auto-apply later requires the ADR-0057 gate regardless of what is recorded here.

*Downstream:* #2091

#### ADR-0062.9 `definition-deletion-policy` — What happens to stored values, audit history and exports when a field definition is deleted? (ADR-0062:80)

*Why it matters.* ADR-0062:80 leaves this open and #2094 requires an "explicit removed-definition policy" that survives an export/import round trip. Hard delete would break proposal diffs, audit old/new value rows and export round-trips that reference the definition.

*Evidence.*

- ADR-0062:60 ("export, import, and deletion behavior" must be defined per field operation), :80 (open question)
- issue #2094 acceptance: "Export/import/deletion round-trip definitions and values with an explicit removed-definition policy."
- docs/STATUS.md:43 (board archive is a soft delete: board.Archive() plus DeleteBehavior.SetNull on captures — archiving made rows unreachable, never destroyed; #1973/PR #2076)

*Options.*

- **A.** Retire (soft-delete) the definition; values retained and hidden from active reads; export includes a definition snapshot; hard purge only via account deletion. — *Consequence:* History, audit and exports stay coherent; costs a retired state in the schema/API/UI and a filter on every read path.
- **B.** Hard delete cascades to values immediately. — *Consequence:* Simplest schema but destroys history and leaves prior proposal diffs and audit rows dangling — against the no-destructive-mutation invariant the archive precedent establishes.
- **C.** Hard delete the definition, keep values as orphaned records with an inline type snapshot. — *Consequence:* Preserves the data but loses validation and typing authority, and import must reconstruct a definition that no longer exists.

*Recommendation:* **A** — Mirrors the board-archive precedent already shipped and proven (STATUS.md:43) and the no-silent-or-destructive-mutation invariant. A retired definition costs one status column plus read filters and satisfies #2094's round-trip acceptance bullet directly. Pair this with the account-deletion clause in the contract amendment below, so "hard purge only via account deletion" has somewhere to be specified.

*Reversibility:* Moderate — B cannot be reversed once values are gone; A can later add an explicit purge operation.

*Downstream:* #2094, #2091

#### ADR-0062.10 `cross-cutting-contract-amendment` — Should ADR-0062's seven-point cross-cutting contract (lines 55-63) be amended to add account deletion, migration bootstrap proof, and rollback behavior, matching its siblings?

*Why it matters.* As written, ADR-0062's seven-point contract (lines 55-63) omits account deletion, migration bootstrap proof and rollback behavior that ADR-0060:78-80 requires. The implementing issues cover these only partially: #2091 and #2093 require migration and rollback (not bootstrap proof or account deletion); #2094 requires migration bootstrap and rollback (not account deletion). So the consistency argument is with ADR-0060, not with 'every issue'; without the amendment a field slice can satisfy ADR-0062 while failing ADR-0060's contract.

*Evidence.*

- ADR-0062:55-63 — the seven points: permission checks and server-side validation; audit and actor attribution; proposal diff and apply semantics; export, import, and deletion behavior; MCP and API representation; realtime invalidation and optimistic-concurrency behavior; SQLite and EF Core migration compatibility
- ADR-0060:78-80 — "permissions, proposal diff/apply behavior, audit and attribution, export/import, account deletion, MCP/API compatibility, realtime invalidation, optimistic concurrency, migration bootstrap proof, and rollback behavior"
- issue #2091 acceptance: "Any accepted schema slice defines permissions, proposal diff/apply, audit, export/import/deletion, MCP/API, realtime, migration, and rollback behavior."
- issue #2094 acceptance: "MCP/API, proposal preview/apply, realtime, concurrency, migration bootstrap, and rollback tests pass."
- issue #2093 acceptance: "Audit, export/import/deletion, MCP/API, realtime, concurrency, migration, and rollback tests pass."

*Options.*

- **A.** Amend ADR-0062:55-63 to add account deletion, migration bootstrap proof, and rollback behavior — making its contract identical to ADR-0060:78-80. — *Consequence:* One ADR edit in the same sitting; the ADR and its three issues then state the same bar, and the deletion-policy ruling above gains an explicit home for "hard purge only via account deletion".
- **B.** Ratify the seven points as written and rely on ADR-0060:78-80 plus the issue acceptance lists to supply the missing three. — *Consequence:* No ADR edit, but a reader of ADR-0062 alone gets a contract three items short of the real bar; a future slice could pass the ADR and fail #2094.
- **C.** Replace ADR-0062:55-63 with a pointer to ADR-0060:78-80 as the single canonical contract. — *Consequence:* Eliminates the drift permanently and keeps one list to maintain, but couples ADR-0062's ratification to ADR-0060 acceptance even for readers who only care about fields.

*Recommendation:* **A** — The gap is real against ADR-0060:78-80 (three named items absent from ADR-0062:55-63) and partially against the issues; A is a one-line text edit that removes the way a slice could pass ADR-0062 and fail ADR-0060, and unlike C it keeps ADR-0062 self-contained. It is an amendment to the ADR text — record it as such.

*Reversibility:* Easy — an ADR text amendment before any schema exists.

*Downstream:* #2091, #2094, #2093, #2084

<details><summary>Sources read for this brief</summary>

- docs/decisions/ADR-0062-custom-fields-aggregates-and-threshold-rules.md (full, 102 lines, line-numbered)
- docs/decisions/ADR-0060-canonical-work-model-and-compatibility-path.md lines 1-100 (line-numbered; :13-14, :46-58, :60-61, :69-71, :78-80, :86-94 verified)
- docs/decisions/ADR-0061-trusted-shared-instance-and-managed-saas-boundary.md lines 1-60 (:23-26 stage 1 / #1772; :42-48 stage 2)
- docs/strategy/PRODUCT_DIRECTION.md lines 44-72 (:49-50 shipped policy, :52 MCP boundary, :54-70 ADR-0057 direction-only) and 94-98 (:96 P10)
- docs/REVIVAL_PLAN.md lines 44-62 (:45-50 milestone table, :57-59 v0.3 M2 and waiver-seeded candidates)
- docs/STATUS.md lines 42, 43, 122, 416, 459 (section-read)
- gh issue view 2091 --json body,comments,labels,milestone,state (OPEN, 0 comments, v0.3, Priority II, decision + human-action)
- gh issue view 2093 --json body,milestone,labels,state (OPEN, v0.3, Priority II)
- gh issue view 2094 --json body,comments,labels,milestone,state (OPEN, 0 comments, v0.3, Priority III)
- gh issue view 1133 / 1307 / 2084 / 1772 --json number,title,state,milestone,labels
- gh api repos/Chris0Jeky/Taskdeck/milestones — v0.2: 0 open / 15 closed, due 2026-09-01; v0.3: 20 open, due 2026-09-09
- backend/src/Taskdeck.Domain/Entities/Card.cs:30-40; Label.cs:13; BoardAccess.cs:55-67; Notification.cs:93-100
- backend/src/Taskdeck.Application/Services/AutomationProposalService.cs:17-36
- git rev-parse origin/main (927236bd0304e9dfae59a7116394e4fcb7b0ec07) and origin/integration/v0.3.0 (ad5325419f34294b361dd711c00d951cb58fb761)
- git grep -l -E 'CustomFieldDefinition|CustomFieldValue|ThresholdRule|class WorkLog' <ref> -- backend/src on both refs → no matches (exit 1)
- grep -rn 'collaboration alpha' --include=*.md . → only ADR-0062:77 plus two .worktrees copies; grep -rn 'small-team alpha' docs/ → ADR-0061:42 and three spike/analysis references

</details>
