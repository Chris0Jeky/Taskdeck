# MVP Expansion Reconciliation Tracker

Date: 2026-03-07
Branch: `docs/mvp-expansion-reconciliation`
Status: Verification complete

## Purpose

Track the reconciliation of the new MVP expansion review packs into the active Taskdeck documentation spine and backlog framing.

## Source Packs

- `docs/InReview/MVP_EXPANSION/MINIMAL/taskdeck_review_2026-03-06/`
- `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/`

## Source Inputs

Primary current-doc inputs:

- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/GOLDEN_PRINCIPLES.md`
- `docs/ISSUE_EXECUTION_GUIDE.md`
- `docs/TESTING_GUIDE.md`
- `docs/MCP_TOOLING_GUIDE.md`
- `docs/GITHUB_PROJECT_AUTOMATION.md`
- `docs/INDEX.md`
- `docs/USER_MANUAL.md`
- `README.md`

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

## Working Synthesis

### Core conclusion

Taskdeck does not need another broad capability brainstorm before the next delivery cycle.
It needs a product-legibility correction that turns the current harness and capture strengths into a product that teaches itself:

- novice-first entry point
- clearer golden path
- board-centered context travel
- readable review surface
- stronger docs/help entry points

### Planning posture adopted for integration

1. Preserve the current product thesis:
   - capture should stay near-zero friction
   - automation remains proposal-first and review-first
   - board execution remains the visible work surface
2. Reframe the next major work as a staged productization track:
   - Phase A: novice-first shell and self-explaining UX
   - Phase B: board-centered daily workflow and review readability
   - Phase C: only then expand into agent substrate and knowledge/integrations
3. Keep advanced/operator surfaces visible in docs, but explicitly secondary to the core MVP loop.

### Review-driven priorities promoted into active docs

- Add a first-class `Home` / start-surface direction.
- Add a `Today` / agenda direction after the start and review surfaces are coherent.
- Replace raw board-ID happy paths with board pickers/search selectors.
- Make proposals legible in plain language with stronger next-step links.
- Require action-oriented empty/help states on main pages.
- Keep agent/runs/knowledge/integrations as planned expansion, not the immediate MVP cycle.

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
- Removed the duplicate interim tracker variant after preserving its high-signal decisions here.

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
- Seeded in the new Wave I pass:
  - `#318` tracker
  - `#320` workspace modes + `Home`
  - `#322` `Review`-first routing, empty/help states, and selectors
  - `#324` `Today` + onboarding path
  - `#326` proposal readability + board-centered action flow
  - `#328` first-run smoke + launch-criteria guardrail
- Updated existing overlap issues in the same pass:
  - `#96` reprioritized to `Priority II` and narrowed to onboarding/help follow-through for the productization wave
  - `#100` reprioritized to `Priority II` and narrowed to user-docs/FAQ follow-through for the productization wave
- Still intentionally not seeded yet:
  - agent profile/run/knowledge foundation slice as described in the new blueprint

## Commands Run

- listed active docs and MVP expansion folders
- read `STATUS.md`, `IMPLEMENTATION_MASTERPLAN.md`, `GOLDEN_PRINCIPLES.md`, `ISSUE_EXECUTION_GUIDE.md`, `TESTING_GUIDE.md`, `MCP_TOOLING_GUIDE.md`, `GITHUB_PROJECT_AUTOMATION.md`
- reviewed `MINIMAL` and `EXPANDED` master/index files plus key backlog/manual/testing/roadmap sections
- searched GitHub issues for overlap on onboarding, search, and new productization themes
- seeded GitHub productization issues:
  - created `#318`, `#320`, `#322`, `#324`, `#326`, `#328`
  - updated `#96`, `#100`, and `#107`
- spot-checked GitHub Project v2 metadata via `gh api graphql`
  - confirmed new wave issues auto-added with `Status=Pending`
  - confirmed new wave issues and updated `#96` / `#100` carry `Priority II`
  - confirmed `#107` remains `Priority V`
  - detected broader legacy `Priority`-field drift outside this new wave; treated as a separate follow-up concern rather than expanding this pass into a full historical cleanup
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
- GitHub Project v2 spot-check -> passed (`Status`/`Priority` fields aligned for `#318`, `#320`, `#322`, `#324`, `#326`, `#328`, `#96`, `#100`, `#107`)

## Decision Log

### 2026-03-07

- Adopt `docs/START_HERE.md` as the preferred new bridge-doc entry point.
- Treat the `MINIMAL` pack as the near-horizon execution filter when `EXPANDED` suggests larger future breadth.
- Keep agent workspace and knowledge/integrations on the roadmap, but sequence them behind novice-first productization.
- Use file-scoped or batch-scoped commits instead of one large documentation commit.
- Add the resulting productization wave to `#107` before active execution begins; do not scatter the new scope across disconnected old issues.
- Keep a single canonical tracker file and fold continuity-critical detail into it instead of maintaining parallel trackers.

### 2026-03-08 deletion audit

- Re-audited commit `849eaaf` to confirm the deletion of `docs/analysis/2026-03-07_mvp-expansion-integration-tracker.md` was intentional and happened in the original consolidation commit, not during context compaction.
- Confirmed the surviving reconciliation tracker is the canonical continuity record and already preserves the high-signal synthesis, source inputs, decision log, and commit ledger that mattered operationally.
- Residual risk from the consolidation was not data loss in Git history; it was future confusion about whether one or two tracker files should remain active. That risk is now treated as resolved: keep only this tracker live.
- Chosen next seeding shape: one dedicated productization wave tracker plus a small set of focused child issues, while updating overlapping existing issues (`#96`, `#100`, `#107`) instead of cloning their scope into new duplicates.
- Seeded the chosen Wave I shape as `#318`, `#320`, `#322`, `#324`, `#326`, and `#328`, with `#96` and `#100` updated into the same `Priority II` tranche.
- Closed the competing parallel-created batch (`#317`, `#319`, `#321`, `#323`, `#325`, `#327`) as duplicates once the overlap was detected, keeping `#318` / `#320` / `#322` / `#324` / `#326` / `#328` as the canonical sequence.

## Commit Ledger

- `8f407bb` - Add MVP expansion reconciliation tracker
- `0871cf0` - Add MVP expansion integration tracker
- `9809cda` - Add MVP expansion gap map
- `9622f07` - Integrate MVP expansion planning spine
- `be5064d` - Add audience-first entry docs
- `ba187e1` - Add start-here and reshape user docs
- `71c43db` - Integrate MVP expansion planning wave
- `b9b77a0` - Refine product guidance and testing docs
- `849eaaf` - Consolidate MVP expansion analysis notes

## Open Questions

- Whether the public-story follow-through in `#216` should be reprioritized immediately with the internal productization wave, or stay linked but secondary until `Home` / `Review` / `Today` are partially shipped.

## Next Actions

1. Keep `docs/analysis/2026-03-07_mvp-expansion-gap-map.md`, `#318`, and `#107` synchronized if the Wave I slice map changes.
2. Decide whether `#216` should stay secondary or move into the same immediate tranche once `Home` / `Review` / `Today` are partially shipped.
3. Decide whether the broader historical GitHub Project `Priority`-field drift should be cleaned in a dedicated ops/backlog-maintenance pass.
