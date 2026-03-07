# MVP Expansion Reconciliation Tracker

Date: 2026-03-07
Branch: `docs/mvp-expansion-reconciliation`
Status: Verification complete

## Purpose

Track the reconciliation of the new MVP expansion review packs into the active Taskdeck documentation spine and backlog framing.

## Source Packs

- `docs/InReview/MVP_EXPANSION/MINIMAL/taskdeck_review_2026-03-06/`
- `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/`

## Primary Goals

1. Integrate the new novice-first MVP/productization direction into canonical docs.
2. Reconcile the new blueprint with current shipped reality so `STATUS.md` stays truthful.
3. Reconcile the new blueprint with current backlog/issues so future execution stays focused and duplicate issue seeding is avoided.
4. Improve documentation entry points for new contributors, evaluators, and future users.

## Working Decisions

- Treat the `MINIMAL` pack as the concise product diagnosis and prioritization source.
- Treat the `EXPANDED` pack as the architectural and issue-seeding expansion layer.
- Keep immediate productization work separate from later agent/knowledge/integration expansion.
- Avoid rewriting `STATUS.md` as aspiration; put future-facing work in roadmap/backlog docs instead.

## Batch Plan

### Batch 1

- Complete.
- Created branch and tracker.
- Read source-of-truth docs and MVP expansion packs.
- Built current-doc gap map.
- Built GitHub issue overlap map.

### Batch 2

- Complete.
- Updated canonical planning docs:
  - `docs/IMPLEMENTATION_MASTERPLAN.md`
  - `docs/STATUS.md`
  - `docs/INDEX.md`
  - `docs/ISSUE_EXECUTION_GUIDE.md`
  - `docs/TaskdeckNextWorkChecklist.md`
- Promoted a clearer novice-first entry point:
  - `docs/START_HERE.md`

### Batch 3

- Complete.
- Updated user-facing/product docs:
  - `README.md`
  - `docs/USER_MANUAL.md`
  - `docs/DOGFOODING_GUIDE.md`
  - `docs/DEMO_PLAYBOOK.md`
  - `docs/SCENARIOS.md`
  - `docs/TESTING_GUIDE.md`
- Added reconciliation notes for issue planning and future batch execution.

### Batch 4

- Complete.
- Consolidated the analysis record to:
  - `docs/analysis/2026-03-07_mvp-expansion-reconciliation-tracker.md`
  - `docs/analysis/2026-03-07_mvp-expansion-gap-map.md`
- Removed the duplicate interim tracker variant to avoid split continuity records.

## Current Findings

### Documentation gaps

- Canonical docs now explicitly record the near-horizon gap as product legibility rather than missing engine capability.
- `docs/START_HERE.md` now bridges repo setup, product thesis, seeded demo flow, and the current golden path.
- `USER_MANUAL.md`, `DOGFOODING_GUIDE.md`, and `DEMO_PLAYBOOK.md` now distinguish the normal capture/review/board path from advanced/operator surfaces.
- `INDEX.md` now includes audience-first read paths.

### GitHub issue overlap

- Existing overlap found:
  - `#96` onboarding/contextual help
  - `#93` global search and quick actions
  - `#100` end-user guides/tutorials/FAQ
  - `#216` thesis-aligned demo and landing baseline
  - `#97` / `#98` for later integration/plugin breadth
- No clear existing issue coverage found yet for:
  - `Home` route / start surface
  - `Today` route
  - workspace presentation modes (`guided`, `workbench`, `agent`)
  - queue board picker / raw-ID removal in common flows
  - proposal summary/readability overhaul
  - empty-state action guidance wave
  - agent profile/run/knowledge foundation slice as described in the new blueprint

## Commands Run

- listed active docs and MVP expansion folders
- read `STATUS.md`, `IMPLEMENTATION_MASTERPLAN.md`, `GOLDEN_PRINCIPLES.md`, `ISSUE_EXECUTION_GUIDE.md`, `TESTING_GUIDE.md`, `MCP_TOOLING_GUIDE.md`, `GITHUB_PROJECT_AUTOMATION.md`
- reviewed `MINIMAL` and `EXPANDED` master/index files plus key backlog/manual/testing/roadmap sections
- searched GitHub issues for overlap on onboarding, search, and new productization themes
- created and committed:
  - tracker baseline
  - dated MVP expansion gap map
  - audience-first entry docs batch
  - planning-wave integration batch
  - product guidance/testing docs batch
- verification:
  - `node scripts/check-docs-governance.mjs`
  - `node scripts/check-github-ops-governance.mjs`

## Verification Log

- `node scripts/check-docs-governance.mjs` -> passed
- `node scripts/check-github-ops-governance.mjs` -> passed

## Next Actions

1. Review the seeded issue guidance when deciding whether to write back the next productization wave into GitHub.
2. Use `docs/analysis/2026-03-07_mvp-expansion-gap-map.md` as the issue-seeding source for the next backlog pass.
