# ADR-0060: Canonical Work Model and Board Compatibility Path

- **Status**: Proposed
- **Date**: 2026-08-26
- **Deciders**: Chris0Jeky (maintainer, decision pending)
- **Related**: `#2084`, `#2087`, `#1947`, `#1321`, ADR-0003, ADR-0005, ADR-0056,
  ADR-0057

## Context

Taskdeck currently persists `Board -> Column -> Card`. A card requires both a board and a column.
Capture, transcript, source artefact, proposal, audit, board access, and agent-run records already
provide strong provenance and review boundaries, but there is no durable Workspace, Project,
WorkItem, WorkRelation, Actor, Assignment, CustomField, or WorkLog model.

The v0.1.2 dogfooding pass asks for projects, hierarchy, typed relations, epics, spikes, ongoing
work, recurrence, templates, multiple assignees, estimates, and fields. Those concepts cannot share
one generic link or tag without creating ambiguous behavior and expensive migrations.

## Proposed decision

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

Every schema stage must define permissions, proposal diff/apply behavior, audit and attribution,
export/import, account deletion, MCP/API compatibility, realtime invalidation, optimistic
concurrency, migration bootstrap proof, and rollback behavior.

## Decisions still required

The maintainer must decide:

1. whether Project becomes first-class before v0.3;
2. whether one work item may appear on several boards;
3. whether parent/child hierarchy may cross projects;
4. the first item types;
5. whether generic custom fields enter v0.3 or later;
6. whether actual time tracking fits the product thesis.

Until decided, the safer defaults are no multi-board placement, no cross-project hierarchy, a
minimal type set, and no generic fields or time tracking.

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

