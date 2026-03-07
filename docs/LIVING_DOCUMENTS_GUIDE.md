# Living Documents Guide

This guide exists for the operator/maintainer workflow.
It identifies the documents that should stay at `docs/` root, what parts of them change over time, and what should trigger an update.

It also calls out the Codex-facing instruction surfaces that must stay aligned with the docs spine.

## Root Living Docs

### `docs/STATUS.md`

Update when:
- shipped behavior changes
- current focus or constraints change
- verified totals or active quality posture change
- major backlog/reconciliation conclusions change

High-churn sections:
- `Last Updated`
- `Current Focus`
- `Project Summary`
- MVP expansion/planning integration notes
- delivered/follow-through status bullets

### `docs/IMPLEMENTATION_MASTERPLAN.md`

Update when:
- execution order changes
- new issue waves are seeded or reprioritized
- release framing changes
- roadmap horizons or active carry-forward rules change

High-churn sections:
- planning rules / decision rules
- roadmap by horizon
- active backlog by priority
- out-of-code/configuration coverage

### `docs/ISSUE_EXECUTION_GUIDE.md`

Update when:
- dependency order changes
- issue waves are added, closed, or reprioritized
- reuse-anchor rules change

High-churn sections:
- implementation notes from current audits
- stage ordering
- execution notes under each stage

### `docs/TaskdeckNextWorkChecklist.md`

Update when:
- promotion rules change
- the seeded wave map changes
- checklist carry-forward needs to reflect new issue numbers

High-churn sections:
- future expansion wave summary
- MVP expansion wave carry-forward
- out-of-code/configuration coverage

### `docs/GOLDEN_PRINCIPLES.md`

Update when:
- a stable repository invariant changes
- a decision rule becomes permanent enough to be treated as governance

High-churn sections:
- `Last Updated`
- principle list

### `docs/TESTING_GUIDE.md`

Update when:
- verified totals change
- required commands or environment posture change
- smoke/gate policy changes
- telemetry/release-gate expectations change

High-churn sections:
- `Current Verified Totals`
- `Product-Coherence Testing Priorities`
- command blocks
- demo tooling policy

### `docs/MANUAL_TEST_CHECKLIST.md`

Update when:
- a new important manual regression path exists
- operator/security/manual UX checks change

High-churn sections:
- product walkthrough steps
- security sanity steps
- ops/manual validation references

### `docs/START_HERE.md`

Update when:
- first-entry guidance changes
- the current golden path changes
- top-level product vocabulary changes

### `docs/USER_MANUAL.md`

Update when:
- shipped navigation or surface meaning changes
- advanced/operator boundaries move
- new normal-user workflow becomes part of the shipped product

### `docs/GITHUB_PROJECT_AUTOMATION.md`

Update when:
- project status model changes
- priority sync policy changes
- GitHub Project workflow automation changes

### `docs/MCP_TOOLING_GUIDE.md`

Update when:
- tool-selection or fallback policy changes
- a new MCP becomes part of the normal operating model

## Codex / Contributor Instruction Surfaces

These are not all inside `docs/`, but they are part of the living operating system for the repo:

- `AGENTS.md`
  - update whenever root-doc paths change or contributor expectations change
- `docs/MCP_TOOLING_GUIDE.md`
  - update when tool selection/fallback rules change
- `docs/GITHUB_PROJECT_AUTOMATION.md`
  - update when project rules/status/priority behavior changes

## Related Stable Maps

- `docs/manual/README.md`
  - update when the user-manual chapter structure, top-level product navigation, or in-app help mapping changes

## What Should Not Stay At Root

Move out of root when a doc is:

- a stable reference
- a runbook for a specific domain (`ops`, `security`, `tooling`, `platform`, `product`)
- a manual chapter or manual-structure reference (`docs/manual/*`)
- a dated analysis snapshot
- historical provenance or migration history

Archive when a doc is:

- superseded
- historical-only
- a transitional migration/provenance note
- a temporary extraction summary

## Update Order When Reality Changes

Use this order to keep the docs spine coherent:

1. `docs/STATUS.md`
2. `docs/IMPLEMENTATION_MASTERPLAN.md`
3. `docs/ISSUE_EXECUTION_GUIDE.md` and/or `docs/TaskdeckNextWorkChecklist.md`
4. `docs/TESTING_GUIDE.md` and `docs/MANUAL_TEST_CHECKLIST.md`
5. user-facing docs (`docs/START_HERE.md`, `docs/USER_MANUAL.md`, plus `docs/product/*`)
6. `docs/INDEX.md`
7. `AGENTS.md` if moved paths or contributor expectations changed

## Transient Working Material

Use these areas for non-root material that is still live:

- `docs/analysis/`
  - dated audits, reconciliation notes, and mapping files
- `docs/InReview/`
  - source packs waiting for promotion/issue seeding/archive
- `docs/WIP/`
  - external or unpromoted working inputs

Promotion rule:
- if a recurring truth is needed every session, it belongs in a root living doc
- if it is a one-off audit or source-pack extraction, keep it out of root
