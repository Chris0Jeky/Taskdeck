# Documentation Index

This directory is the canonical documentation entrypoint for Taskdeck.

Root `docs/` is reserved for living documents that change regularly or are direct operator, contributor, or user entrypoints.
Stable reference material belongs in topical subfolders.

## Root Living Docs

- `STATUS.md`
  - current shipped reality, verified state, current focus, and active constraints
- `IMPLEMENTATION_MASTERPLAN.md`
  - forward execution roadmap, priority map, and near-horizon sequencing
- `GOLDEN_PRINCIPLES.md`
  - stable repository invariants and decision rules
- `ISSUE_EXECUTION_GUIDE.md`
  - dependency-aware issue order and execution protocol
- `TaskdeckNextWorkChecklist.md`
  - lightweight promotion and wave-checklist companion to the masterplan and issue guide
- `TESTING_GUIDE.md`
  - automated verification commands, smoke posture, and release-gate guidance
- `MANUAL_TEST_CHECKLIST.md`
  - human verification checklist for product, security, and ops flows
- `START_HERE.md`
  - audience-first first-entry guide for the shipped `Home` / `Today` / `Inbox` / `Review` / `Boards` shell
- `USER_MANUAL.md`
  - current shipped product reference, workflow guide, FAQ, and troubleshooting baseline
- `GITHUB_PROJECT_AUTOMATION.md`
  - canonical GitHub Project status, priority, and workflow rules
- `MCP_TOOLING_GUIDE.md`
  - canonical MCP selection and fallback rules for agents
- `LIVING_DOCUMENTS_GUIDE.md`
  - root-doc change map: what changes often, what to update, and in what order

## Recommended Read Paths

- New user or evaluator:
  - `START_HERE.md` -> `USER_MANUAL.md` -> `product/DEMO_PLAYBOOK.md`
- Daily user who wants the help-center/manual map:
  - `START_HERE.md` -> `USER_MANUAL.md` -> `manual/README.md`
- Maintainer or planner:
  - `STATUS.md` -> `IMPLEMENTATION_MASTERPLAN.md` -> `ISSUE_EXECUTION_GUIDE.md` -> `TESTING_GUIDE.md`
- Contributor or agent:
  - `STATUS.md` -> `IMPLEMENTATION_MASTERPLAN.md` -> `GOLDEN_PRINCIPLES.md` -> `ISSUE_EXECUTION_GUIDE.md` -> `MCP_TOOLING_GUIDE.md`
- Demo operator:
  - `START_HERE.md` -> `product/DEMO_PLAYBOOK.md` -> `product/SCENARIOS.md` -> `product/DOGFOODING_GUIDE.md`

## Topical Folders

- `product/`
  - product-facing guides, demo playbook, scenario reference, and dogfooding cadence
- `manual/`
  - manual structure map, in-app help mapping, and future chapter split rules
- `ops/`
  - deployment, observability, human-operator runbooks, and session-start checklists
- `platform/`
  - provider, import-adapter, and starter-pack platform/reference docs
- `security/`
  - active security and abuse-protection policies and baselines
- `tooling/`
  - MCP operations, harness guidance, and deferred tooling backlog
- `analysis/`
  - dated reconciliation notes, audits, and planning snapshots
  - non-authoritative unless promoted into the canonical docs above
- `InReview/`
  - human or in-review source packs awaiting extraction into canonical docs or issues
- `archive/`
  - historical and superseded docs; non-authoritative by default
- `WIP/`
  - external or unpromoted working material that has not yet been reconciled

## Working Notes

- `analysis/2026-03-07_mvp-expansion-reconciliation-tracker.md`
  - continuity log for the MVP expansion reconciliation
- `analysis/2026-03-07_mvp-expansion-source-coverage-audit.md`
  - file-by-file and snippet-by-snippet audit for `docs/InReview/MVP_EXPANSION/`
- `analysis/2026-03-07_mvp-expansion-gap-map.md`
  - Wave P / Q / R backlog mapping and reuse-anchor summary
- `analysis/2026-03-07_mvp-productization-seeding-plan.md`
  - issue-seeding and duplicate-resolution record for the productization wave

## Governance Rules

- Do not add new root docs by default.
- If a document is stable reference material rather than a living source of truth, put it in a topical folder.
- If a note is historical, archive it instead of leaving it at root.
- Every meaningful behavior or workflow change should update:
  1. `STATUS.md`
  2. `IMPLEMENTATION_MASTERPLAN.md`
  3. `ISSUE_EXECUTION_GUIDE.md` when order or dependencies change
  4. `TESTING_GUIDE.md` or `MANUAL_TEST_CHECKLIST.md` when verification changes
- Keep root `docs/` readable at a glance; use `LIVING_DOCUMENTS_GUIDE.md` before promoting new recurring docs into root.
