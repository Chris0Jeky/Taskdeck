# ADR-0062: Custom Fields, Aggregates, and Threshold Rules

- Status: Accepted (ratified by the maintainer in-session on 2026-08-29 — guided-walkthrough
  reply q-4 A, decision map map:v1:3b5e90e6a5e9b97b362dbe5d8412b699b742818a054edd8443515fc2c2dfe3e7,
  recorded on #2091; not inferred from the implementation or its tests)
- Date: 2026-08-26
- Deciders: Chris0Jeky (maintainer)
- Related: #2091, #2094, ADR-0060

## Context

Dogfooding identified a need for estimates, deadlines, stages, time measures, coloured values, totals, and threshold flags. These concepts have different storage, calculation, and automation behavior. Treating them as tags or one generic value would obscure type safety, provenance, permissions, and proposal review.

The current shipped model has no generic custom-field, work-log, aggregate, or threshold-rule entities. This decision therefore defines a semantic boundary. It does not describe shipped behavior or authorize implementation before the canonical work-model decision in ADR-0060 is accepted.

## Decision

### Field definitions and values

Use a `CustomFieldDefinition` to define a field's name, type, scope, constraints, and display metadata. Store values separately as typed `CustomFieldValue` records associated with a stable work item.

The initial type vocabulary may include:

- text;
- number and decimal;
- duration;
- date;
- boolean;
- single-select and multi-select;
- participant reference;
- URL.

Formula fields are excluded from the first implementation slice. Colour is presentation metadata for a definition or select option, not the stored meaning of every field.

### Built-in work measures

Common work measures may be built-in fields where shared semantics and indexing matter. They remain distinct:

- estimated effort: expected work required;
- remaining effort: current forecast of unfinished work;
- actual work logged: immutable recorded activity, if work logs are accepted;
- elapsed cycle time: derived time between lifecycle events;
- lead time: derived time from request or capture to completion;
- story points or relative size: comparative estimate, not elapsed time;
- capacity or allocation: actor availability or planned share, not task duration.

Assignment does not imply work logged, and adding assignees must not apply an automatic duration formula.

### Aggregates and rules

Aggregates are derived read models or queries over authoritative values and events. They are not custom-field values that clients write directly.

Threshold and automation rules are separate definitions that evaluate fields or aggregates. A rule may produce a flag or notification. Any rule that proposes a domain-state mutation must use the existing proposal, review, and apply path. No rule can directly approve or mutate a board.

### Cross-cutting contract

Every field operation must define:

- permission checks and server-side validation;
- audit and actor attribution;
- proposal diff and apply semantics;
- export, import, and deletion behavior;
- account-deletion behavior;
- MCP and API representation;
- realtime invalidation and optimistic-concurrency behavior;
- SQLite and EF Core migration compatibility;
- migration bootstrap proof;
- rollback behavior.

Historical effort by actor or stage requires an immutable work-log or lifecycle-event model. Current column membership is current state and cannot prove past activity.

## Sequencing

1. The ADR-0062 decisions are recorded on #2091; generic fields are deferred, as recorded below.
2. Ship only the narrowly defined built-in estimate (#2093) before a generic field system, and only
   after ADR-0060 is Accepted.
3. Specify definition scope, value typing, deletion behavior, and proposal operations.
4. Add aggregate read models after authoritative values and lifecycle events exist.
5. Add threshold rules only after notification and automation governance is defined.

## Decisions recorded (2026-08-29)

Ratified by the maintainer in-session on 2026-08-29 (guided-walkthrough reply q-4 A, decision map
map:v1:3b5e90e6a5e9b97b362dbe5d8412b699b742818a054edd8443515fc2c2dfe3e7, recorded on #2091). These
answers replace the four questions this ADR previously left open. Recording them authorizes no
implementation.

- **custom-field-timing** — B: ship #2093 in v0.3 — the Principal/Participant boundary, multiple
  assignments, one built-in estimate, current-state roll-ups by participant and board/column, and
  acting-principal attribution — once ADR-0060 is Accepted; defer the generic typed custom-field
  foundation (#2094) to after ADR-0061 stage 2 "Dependable small-team alpha", which replaces this
  ADR's previously undefined phrase "after the collaboration alpha". Prerequisites and consequences:
  nothing starts until ADR-0060 is Accepted (#2084); #2093 is itself an identity and assignment
  schema plus migration, carrying its own audit, export/import/deletion, MCP/API, realtime,
  concurrency, migration and rollback bar, not a single nullable column; #2094 is re-milestoned off
  v0.3.
  *Timing amendment (2026-08-30, v0.3 RC deck q-3 = B, recorded on #2093): the "ship #2093 in v0.3"
  slot moves to v0.4; only the multiple-assignments sub-slice (#2240 — no estimates, no roll-ups, no
  new participant record) stays in v0.3. The ruling's content, prerequisites and gate are unchanged.*
- **adr0062-gate-on-2093** — A: ADR-0062 does gate #2093's estimate and roll-up half — #2093 may not
  start until ADR-0060 is Accepted and both the built-in-measure boundary and the derived-aggregate
  rule above are ratified. Consequence: the estimate cannot ship as a mutable "actual hours" number
  and roll-ups cannot ship as stored writable summaries. The ruling is recorded on #2091; #2093's
  Dependencies and this ADR's Related list are deliberately left unedited.
- **first-type-vocabulary** — A: the first generic-field slice carries #2094's six types — text,
  number, date, boolean, single-select, and URL. Maintainer scope note: "six types as the first
  slice — an application of the ADR's optional 'may include' list, NO change to that list." Duration
  stays only in the built-in estimate and participant references only in Assignment; multi-select,
  duration, and participant fields remain later additive types.
- **field-scope-ownership** — A: definitions are board-scoped, stored with an explicit scope-kind and
  scope-id pair so that re-parenting them to a Project or Workspace boundary later is a data
  migration rather than a redesign. Consequence: this reuses the shipped board-access permission
  model and the label precedent, and definitions are duplicated across boards until a Project
  boundary exists.
- **field-management-permission-role** — A: creating, editing, or retiring a field definition
  requires the owner-or-Admin level; writing field values requires owner-or-write; reading requires
  owner-or-read. Maintainer scope note: "owner-or-access predicates" — every check is expressed
  through the authorization service's owner-or-access predicates, never through a board-access row
  alone, because an owner deliberately holds no such row. Cost: one extra authorization branch and a
  distinct 403 path in the API and MCP surfaces.
- **actual-time-tracking-fit** — A: an immutable, attributed, optional WorkLog is accepted
  conceptually but excluded from v0.3 and from the alpha. Estimate stays a built-in mutable value,
  and "actual" may only ever come from a WorkLog or from audit-derived lifecycle events — never from
  a mutable "actual hours" column. Consequence: no project-management weight ships, and workload
  reports keep stating that they are assignment and estimate totals, not historical activity.
- **aggregate-rollup-semantics** — A: aggregates and roll-ups are derived on read — a query or read
  model over authoritative values and events — never persisted and never client-written, and always
  labelled as current-state totals. Consequence: one source of truth and no invalidation bugs; the
  cost is query time, which the existing metrics services already accept at SQLite single-instance
  scale. A cache may later be layered under the same read contract.
- **threshold-rule-semantics** — B: a rule that fires may flag or notify, or emit a proposal through
  the existing proposal, review, and apply path. Maintainer scope note: "flag/notify or emit a
  proposal through the review path; auto-apply excluded, timing not decided." Direct apply stays
  excluded and belongs to a separately gated ADR-0057 slice; a rule needs a rule-principal identity
  in audit, and its operations must map onto the existing known-action-verb vocabulary. This
  ratifies an envelope, not a release: implementation still follows sequencing step 5.
- **definition-deletion-policy** — A: deleting a definition retires it. Values are retained and
  hidden from active reads, export includes a definition snapshot, and a hard purge happens only
  through account deletion. Cost: a retired state in the schema, API, and UI, plus a filter on every
  read path. This pairs with the account-deletion item added to the cross-cutting contract.
- **cross-cutting-contract-amendment** — A: a ratified amendment to this ADR's text — the "Every
  field operation must define" list above gains account-deletion behavior, migration bootstrap
  proof, and rollback behavior, making this contract identical to ADR-0060's per-stage checklist so
  that a slice cannot pass ADR-0062 and fail ADR-0060.

## Consequences

- Field types, metrics, and rules remain legible and independently testable.
- Built-in estimates can ship without committing to a full formula language.
- Workload reports cannot claim historical activity until event or work-log evidence exists.
- Threshold automation remains review-first for state changes.
- This decision adds no schema and creates no release commitment on its own; the timing recorded
  above governs when any slice may start.

## Alternatives considered

### Store metrics as coloured tags

Rejected. Tags do not provide numeric typing, duration semantics, aggregation, validation, or reliable proposal diffs.

### Use one polymorphic value and rule table

Rejected for the first slice. Combining storage, derivation, and automation makes permissions, migration, and audit behavior ambiguous.

### Include formulas immediately

Deferred. Formula evaluation, dependency cycles, portability, and safe recalculation are a separate design problem.
