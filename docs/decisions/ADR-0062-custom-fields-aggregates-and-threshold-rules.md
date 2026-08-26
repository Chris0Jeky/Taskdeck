# ADR-0062: Custom Fields, Aggregates, and Threshold Rules

- Status: Proposed
- Date: 2026-08-26
- Deciders: Maintainer ratification pending
- Related: #2091, #2094, ADR-0060

## Context

Dogfooding identified a need for estimates, deadlines, stages, time measures, coloured values, totals, and threshold flags. These concepts have different storage, calculation, and automation behavior. Treating them as tags or one generic value would obscure type safety, provenance, permissions, and proposal review.

The current shipped model has no generic custom-field, work-log, aggregate, or threshold-rule entities. This decision therefore defines a proposed semantic boundary. It does not describe shipped behavior or authorize implementation before the canonical work-model decision in ADR-0060 is accepted.

## Proposed decision

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
- MCP and API representation;
- realtime invalidation and optimistic-concurrency behavior;
- SQLite and EF Core migration compatibility.

Historical effort by actor or stage requires an immutable work-log or lifecycle-event model. Current column membership is current state and cannot prove past activity.

## Sequencing

1. Record the ADR-0062 decisions in #2091 and decide whether generic fields belong in v0.3 or later.
2. If needed, ship only narrowly defined built-in estimates before a generic field system.
3. Specify definition scope, value typing, deletion behavior, and proposal operations.
4. Add aggregate read models after authoritative values and lifecycle events exist.
5. Add threshold rules only after notification and automation governance is defined.

## Human decisions required

- Do generic custom fields belong in v0.3 or after the collaboration alpha?
- At which accepted boundary are definitions owned: workspace, project, or another scope?
- Is actual time tracking part of Taskdeck's context-to-action thesis or unwanted project-management weight?
- What deletion policy preserves historical values and export compatibility?

## Consequences

- Field types, metrics, and rules remain legible and independently testable.
- Built-in estimates can ship without committing to a full formula language.
- Workload reports cannot claim historical activity until event or work-log evidence exists.
- Threshold automation remains review-first for state changes.
- This proposal adds no schema and creates no release commitment until accepted.

## Alternatives considered

### Store metrics as coloured tags

Rejected. Tags do not provide numeric typing, duration semantics, aggregation, validation, or reliable proposal diffs.

### Use one polymorphic value and rule table

Rejected for the first slice. Combining storage, derivation, and automation makes permissions, migration, and audit behavior ambiguous.

### Include formulas immediately

Deferred. Formula evaluation, dependency cycles, portability, and safe recalculation are a separate design problem.
