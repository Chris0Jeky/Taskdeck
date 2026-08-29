# ADR-0060: Canonical Work Model and Board Compatibility Path

- **Status**: Accepted (ratified by the maintainer in-session on 2026-08-29 — guided-walkthrough
  reply q-2 B with scope notes, decision map
  `map:v1:3b5e90e6a5e9b97b362dbe5d8412b699b742818a054edd8443515fc2c2dfe3e7`, recorded on `#2084`;
  not inferred from the implementation or its tests)
- **Date**: 2026-08-26
- **Deciders**: Chris0Jeky (maintainer)
- **Related**: `#2084`, `#2087`, `#2092`, `#2093`, `#2094`, `#2185`, `#1947`, `#1321`,
  `#2187`, `#2188`, ADR-0003, ADR-0005, ADR-0056,
  ADR-0057

## Context

Taskdeck currently persists `Board -> Column -> Card`. A card requires both a board and a column.
Capture, transcript, source artefact, proposal, audit, board access, and agent-run records already
provide strong provenance and review boundaries, but there is no durable Workspace, Project,
WorkItem, WorkRelation, Actor, Assignment, CustomField, or WorkLog model.

The v0.1.2 dogfooding pass asks for projects, hierarchy, typed relations, epics, spikes, ongoing
work, recurrence, templates, multiple assignees, estimates, and fields. Those concepts cannot share
one generic link or tag without creating ambiguous behavior and expensive migrations.

## Decision

Use this target vocabulary:

| Concept | Responsibility |
| --- | --- |
| Workspace | Ownership, security, collaboration, defaults, and a possible future billing boundary |
| Project | Durable outcome and context container |
| Work item | Stable actionable or trackable record |
| Board | Configurable work surface over a bounded set of work items |
| Column | Board-specific lane and current placement, not historical activity |
| Capture/source artefact | Provenance-bearing input that may produce or attach to work |
| Collection/tag | Optional cross-cutting classification |

Hierarchy and relations remain separate:

- one optional parent relation represents containment and uses an adjacency list with cycle checks;
- `WorkRelation` represents typed edges such as blocks, relates to, duplicates, spawned from, and
  depends on;
- item type is independent of either relationship;
- a template creates an independent item and records template-origin provenance;
- recurrence generates independent instances from a rule;
- ongoing lifecycle means durable work without a fixed end and does not imply recurrence.

The recorded bounds on hierarchy scope and depth, on parent lifecycle, and on typed-edge scope are
in *Decisions recorded (2026-08-29)* below.

Identity, participation, presentation, assignment, and recorded work also remain separate:

- `Principal` is the authenticated identity that can receive authority. It may represent a human
  user, an agent, or a service identity. Audit records attribute the acting principal.
- `Participant` is a context-scoped collaboration record for a workspace, project, or compatible
  board boundary. It connects a principal, or a pending external invite, to membership state and
  collaboration metadata. Participation does not itself grant authority outside the owning scope.
- `Persona` is optional display metadata used for name, avatar, or agent presentation. It never
  supplies authentication, authorization, assignment, or audit identity.
- `Assignment` links a participant to a work item and may carry an explicit role or allocation.
  It represents responsibility, not completed work or an automatic scheduling formula.
- `WorkLog` is an immutable activity record attributed to both the participant and acting
  principal. It is separate from assignment, estimate, current column, and elapsed time.
- estimate, remaining effort, actual work logged, elapsed cycle time, lead time, relative size,
  and capacity are different values and calculations.

For the first slice these terms map onto the shipped user and board-authorization model rather than
new identity tables; see the `participant-substrate` ruling below.

No agent or LLM receives a direct mutation path through this model. Automation-originated changes
continue through proposal, review, approve, and execute.

## Compatibility stages

1. **Semantic contract only.** Accept or revise this ADR before schema work.
2. **Card-compatible additions.** Add the smallest accepted item types and one optional parent to
   the existing Card path. Preserve card IDs, board and column ownership, proposal operations,
   audit history, exports, and current API clients.
3. **Separate depth features.** Add typed relations, Principal/Participant/Assignment foundations,
   and built-in estimates as independent migrations only when their issues are admitted. WorkLog
   remains a later event-model decision.
4. **Lightweight Project boundary.** If ratified, introduce Project around existing boards without
   making boards arbitrary views or moving every card in the same migration.
5. **Canonical work item and placements.** Consider separating stable work identity from board
   placement only after collaboration and dogfooding evidence justify the migration. Multi-board
   placement is not implied by earlier stages.

Stages 1-3 are ratified as executable direction. Stages 4-5 remain Proposed direction and may be
executed only after an explicit amendment to this ADR; no per-issue ruling substitutes for that
amendment.

Every schema stage must define permissions, proposal diff/apply behavior, audit and attribution,
export/import, account deletion, MCP/API compatibility, realtime invalidation, optimistic
concurrency, migration bootstrap proof, and rollback behavior.

Card-ID preservation in stage 2 binds in-place migrations. Board JSON export/import mints fresh card
identifiers, so it cannot round-trip the original IDs.

## Decisions recorded (2026-08-29)

**project-timing** — A: Project does not become first-class before v0.3 final. This ADR ratifies the
vocabulary only; Project stays compatibility stage 4 and needs its own admitted issue plus
collaboration evidence from `#1772`. Board remains the project proxy, as `#1321` already assumes,
and no ownership migration is attempted inside the v0.3 release band.

**multi-board-identity** — A: One work item is exactly one board and one column through v0.3, and
identity stays the card GUID; separating stable work identity from board placement (stage 5) needs
its own ruling. This neither grants nor forbids cross-board typed edges — that is `relation-scope`
below. Maintainer scope note: "let's keep this for an architecture review for v0.3.0 or later" —
`#2187` is seeded for an architecture review of multi-board identity and
hierarchy boundaries, targeted at v0.3.0 or later, and A holds until that review records a change.

**hierarchy-boundaries** — A: Parent/child hierarchy is same-board only, one optional parent, a hard
depth cap of 3, a server-side cycle check, and type-agnostic — any admitted type may parent any
admitted type, per the item-type independence rule above. Cross-project hierarchy is recorded as
"no", vacuously while no Project entity exists, to be re-decided if stage 4 is ever ratified.
Maintainer scope note: "same as previous" — this ruling is included in the same architecture review,
`#2187`, and holds until that review records a change.

**parent-lifecycle** — A: The default is detach, never cascade — deleting or archiving a parent
clears the child's parent pointer, and children keep their IDs, board, column, history, and
exports. Maintainer scope note: "with ability to cascade through a prompt" — a cascade-archive of
the subtree is permitted only as an explicit, user-confirmed action behind a prompt that names the
affected count, never silent and never on delete. Because detach is a derived mutation of every
child, proposal preview, apply, and audit must list the child pointer changes. Prerequisite: the
shipped archive-card proposal operation applies as a silent no-op today (`#2185`), so a real
card-archive state and handler must exist and be proven before child behavior is defined on top
of it.

**first-item-types** — A: The first item types are Task, Epic, and Spike; all existing cards default
to Task; Bug, Decision, and Ongoing are deferred, and ongoing remains lifecycle rather than a type.
The schema-v2 triage `type` (action, decision, question) is a different axis and needs its own
mapping rather than reuse of this set.

**compat-path** — A: Stages 1-3 are ratified as executable direction — additive columns and
tables on the Card path, with a tested down path and card IDs preserved in place. Stages 4-5 stay Proposed
direction and may be executed only after an explicit amendment to this ADR; no per-issue ruling
substitutes for that amendment. Maintainer scope note: "let's seed a review of this part" —
`#2188` is seeded to review the compatibility ladder, the stage 4-5 gating, and
the card-ID-preservation clarification above.

**participant-substrate** — A: For v0.3 the shipped user is the principal, and participation is the
shipped board-authorization set — board ownership OR a board-access row. Assignment references the
card and the user and is validated through that same owner-or-access check, never against a
board-access row alone, so assigning a card to the board's own owner works. No new identity tables
until `#1772` evidence; invites and pending external participants are out of scope, and a later
Participant model must materialize the owner-or-access union by a data migration (no single
shipped table holds it: owners hold no `BoardAccess` row), preserving that union rather than
renaming either table.

**relation-scope** — A: In the first typed-link slice both endpoints must share a board, and a
cross-board endpoint fails server-side with a stable error. Scope validation stays one equality
check, dangling-edge rules stay inside one board's export, realtime invalidation stays per-board,
and archived-board state is evaluated once. Widening the scope later is a predicate change; nothing
recorded here forbids revisiting it.

**custom-fields-timing** — A: No generic custom fields in v0.3; this ADR records "later". `#2094`
stays conditional and blocked, and `#2091`/ADR-0062 keep only the design questions this ADR never
poses — definition ownership scope, value typing, deletion policy preserving historical values, and
export compatibility. This is a timing ruling, not a rejection: admitting a minimal field slice
later takes an amendment here plus a `#2091` ruling.

**time-tracking-fit** — A: Actual time tracking is not in the thesis for now — no WorkLog and no
actual-time capture before the collaboration alpha, and only the built-in estimate in `#2093`
ships. The same question posed in ADR-0062 is answered here. Admitting a WorkLog event model later
is an additive decision plus an admitted issue.

## Alternatives considered

**Keep Board as the permanent universal parent.** Lowest migration cost, but it makes Project,
cross-board context, and stable work identity harder to add coherently.

**Rewrite immediately to Workspace -> Project -> WorkItem -> View.** Cleaner target shape, but too
risky for v0.2/v0.3 because it simultaneously changes persistence, proposals, exports, MCP, audit,
realtime, and UI ownership.

**Use one generic link field.** Rejected because hierarchy, dependency, provenance, placement, and
template origin have different validation and lifecycle rules.

**Adopt a graph database.** Rejected. EF Core, SQLite, adjacency lists, and typed edge tables are
sufficient for the expected scale and preserve the modular monolith.

## Consequences

- Existing boards and cards remain the shipped model until a later migration is implemented.
- The target vocabulary can guide issue boundaries without claiming planned entities exist.
- Schema growth is incremental and reversible, at the cost of temporary compatibility adapters.
- Templates, recurrence, custom fields, work logs, and board-independent canonical items remain
  deferred until separately admitted.
- Stages 4-5 stay gated behind an amendment to this ADR; `#2188` reviews the
  ladder and that gating.
- The multi-board-identity and hierarchy-boundaries rulings hold as recorded until the architecture
  review seeded as `#2187` records a change.
- Parent archive and delete behavior depends on a real card-archive operation, so `#2185` is a
  prerequisite for the `#2087` slice that defines child behavior.

