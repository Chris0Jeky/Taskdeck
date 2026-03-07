# Documentation Index

This directory is the canonical documentation entrypoint for Taskdeck.

Root `docs/` is reserved for living documents that change regularly or are direct operator/contributor entrypoints.
Stable reference material belongs in topical subfolders.

## Root Living Docs

- `STATUS.md`
  - Current shipped reality, verified state, current focus, and active constraints.
- `IMPLEMENTATION_MASTERPLAN.md`
  - Forward execution roadmap, horizons, release framing, and priority-labeled backlog map.
- `GOLDEN_PRINCIPLES.md`
  - Stable repository invariants and decision rules.
- `ISSUE_EXECUTION_GUIDE.md`
  - Dependency-aware issue order and execution protocol.
- `TaskdeckNextWorkChecklist.md`
  - Lightweight promotion and wave-checklist companion to the masterplan/issue guide.
- `TESTING_GUIDE.md`
  - Automated verification commands, current totals, smoke posture, and release-gate guidance.
- `MANUAL_TEST_CHECKLIST.md`
  - Human verification checklist for product, security, and ops flows.
- `START_HERE.md`
  - Audience-first first-entry product guide.
- `USER_MANUAL.md`
  - Current shipped product reference.
- `GITHUB_PROJECT_AUTOMATION.md`
  - Canonical GitHub Project status/priority/workflow rules.
- `MCP_TOOLING_GUIDE.md`
  - Canonical MCP selection/fallback rules for agents.
- `LIVING_DOCUMENTS_GUIDE.md`
  - Root-doc change map: what changes often, what to update, and in what order.

## Recommended Read Paths

- New user / evaluator:
  - `START_HERE.md` -> `USER_MANUAL.md` -> `product/DEMO_PLAYBOOK.md`
- Maintainer / planner:
  - `STATUS.md` -> `IMPLEMENTATION_MASTERPLAN.md` -> `ISSUE_EXECUTION_GUIDE.md` -> `TESTING_GUIDE.md`
- Contributor / agent:
  - `STATUS.md` -> `IMPLEMENTATION_MASTERPLAN.md` -> `GOLDEN_PRINCIPLES.md` -> `ISSUE_EXECUTION_GUIDE.md` -> `MCP_TOOLING_GUIDE.md`
- Demo operator:
  - `START_HERE.md` -> `product/DEMO_PLAYBOOK.md` -> `product/SCENARIOS.md` -> `product/DOGFOODING_GUIDE.md`

## Topical Folders

- `product/`
  - Product-facing guides, demo playbook, scenario reference, and dogfooding cadence.
- `manual/`
  - Planned manual chapter map, split rules, and in-app help-to-manual mapping.
- `ops/`
  - Deployment, observability, human operator runbooks, and session-start checklists.
- `platform/`
  - Provider, import-adapter, and starter-pack platform/reference docs.
- `security/`
  - Active security and abuse-protection policies/baselines.
- `tooling/`
  - MCP operations, harness/tooling guidance, and deferred tooling backlog.
- `analysis/`
  - Dated reconciliation notes, audits, and planning snapshots (non-authoritative unless promoted).
- `InReview/`
  - Human/in-review source packs awaiting extraction into canonical docs or issue waves.
- `archive/`
  - Historical and superseded docs. Non-authoritative by default.
- `WIP/`
  - External or unpromoted working material that has not yet been reconciled.

## Working Notes

- `analysis/2026-03-07_mvp-expansion-reconciliation-tracker.md`
  - Canonical continuity log for the MVP expansion reconciliation.
- `analysis/2026-03-07_mvp-expansion-source-coverage-audit.md`
  - File-by-file and snippet-by-snippet audit for `docs/InReview/MVP_EXPANSION/`.
- `analysis/2026-03-07_mvp-expansion-gap-map.md`
  - Current Wave P / Q / R backlog mapping and reuse-anchor summary.

## Governance Rules

- Do not add new root docs by default.
- If a document is stable reference material rather than a living source-of-truth, put it in a topical folder.
- If a note is historical, archive it instead of leaving it at root.
- Every meaningful behavior or workflow change should update:
  1. `STATUS.md`
  2. `IMPLEMENTATION_MASTERPLAN.md`
  3. `ISSUE_EXECUTION_GUIDE.md` when order/dependencies change
  4. `TESTING_GUIDE.md` / `MANUAL_TEST_CHECKLIST.md` when verification changes
- Keep root `docs/` readable at a glance; use `LIVING_DOCUMENTS_GUIDE.md` before promoting new recurring docs into root.
