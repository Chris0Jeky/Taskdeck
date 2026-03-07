# MVP Expansion Integration Tracker

Date: 2026-03-07
Branch: `docs/mvp-expansion-integration`
Owner: Codex
Status: In progress

## Purpose

Track the integration of the new MVP expansion review packages into the repository's active docs and backlog guidance.
This file is the working continuity record for:

- source inputs reviewed
- key decisions adopted
- documentation batches
- GitHub issue overlap and seeding notes
- commit ledger for this branch

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

New review inputs:

- `docs/InReview/MVP_EXPANSION/MINIMAL/taskdeck_review_2026-03-06/*`
- `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/*`

## Working Synthesis

### Core conclusion

Taskdeck's next cycle should not focus on adding broad new capability families first.
It should convert current harness strength into a product that teaches itself:

- novice-first entry point
- clearer golden path
- board-centered context travel
- readable review surface
- stronger docs entry points

### Planning posture adopted for integration

1. Preserve the current product thesis:
   - capture should be near-zero friction
   - automation remains proposal-first and review-first
   - board execution remains the visible work surface
2. Reframe the next major work as a staged productization track:
   - Phase A: novice-first shell and self-explaining UX
   - Phase B: board-centered daily workflow and review readability
   - Phase C: only then expand into agent substrate and knowledge/integrations
3. Keep advanced/operator surfaces visible in docs, but explicitly secondary to the core MVP loop.

### Review-driven priorities to integrate into active docs

- Add a first-class `Home`/start surface direction.
- Add a `Today`/agenda direction after the start surface and review surface are coherent.
- Replace raw board ID happy paths with board pickers/search selectors.
- Make proposals legible in plain language with stronger next-step links.
- Require action-oriented empty states on main pages.
- Keep agent/runs/knowledge/integrations as planned expansion, not the immediate MVP cycle.

## Documentation Batch Plan

### Batch 1: source-of-truth and planning spine

- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/ISSUE_EXECUTION_GUIDE.md`
- `docs/INDEX.md`
- `README.md`

Goals:

- reflect the post-demo-expansion productization direction
- codify sequencing and non-goals
- make the new doc entry points discoverable

### Batch 2: onboarding and manual structure

- add `docs/START_HERE.md`
- update `docs/USER_MANUAL.md`
- update `docs/DOGFOODING_GUIDE.md`

Goals:

- create a true bridge doc for first 15 minutes
- separate novice flows from advanced/operator surfaces
- make the golden path explicit near the top

### Batch 3: testing/demo/docs governance alignment

- `docs/TESTING_GUIDE.md`
- `docs/DEMO_PLAYBOOK.md`
- `docs/SCENARIOS.md`

Goals:

- align test priorities to product coherence
- clarify deterministic smoke expectations around the real user story
- keep the harness framed as a product asset, not the product itself

### Batch 4: issue and backlog reconciliation

- GitHub issue overlap review
- update docs with dependency-aware issue groupings and seeded workstreams

Goals:

- avoid drift between blueprint recommendations and repo planning
- make the next issue wave easier to execute without re-deriving scope

## GitHub Issue Reconciliation

Status: In progress

Known review-driven buckets to map against existing issues:

- novice-first shell and workspace modes
- home/start surface and first-run onboarding
- today/agenda view
- board context propagation and action rail
- proposal summary/readability
- board picker replacement for raw IDs
- empty-state/help block quality pass
- demo tools and guided narrative mode
- agent profile/run/run-events substrate
- knowledge docs/search/integrations surface
- telemetry/launch criteria and docs governance updates

## Decision Log

### 2026-03-07

- Adopt `docs/START_HERE.md` as the preferred new bridge doc name.
- Treat the `MINIMAL` package as the near-horizon execution filter when `EXPANDED` suggests larger future breadth.
- Keep agent workspace and knowledge/integrations in the active roadmap, but sequence them after novice-first productization.
- Use file-scoped or batch-scoped commits instead of one large documentation commit.

## Progress Log

### Completed

- Read required repository governance docs and current source-of-truth planning docs.
- Reviewed the new `MINIMAL` and `EXPANDED` package structure and key blueprint files.
- Created branch `docs/mvp-expansion-integration`.
- Created this tracker.

### In progress

- Compare blueprint recommendations against current docs and GitHub backlog.
- Draft Batch 1 updates for source-of-truth planning docs.

### Pending

- Add/reshape user-facing onboarding docs.
- Update testing/demo docs to reflect the novice-first roadmap.
- Reconcile issue coverage and capture issue seeding guidance.

## Commit Ledger

- Pending

## Follow-ups / Open Questions

- Determine whether README should be fully repositioned around `Home/Inbox/Review/Projects` now or only lightly adjusted until UI routes ship.
- Decide how much agent-workspace detail should appear in `STATUS.md` versus remaining mostly roadmap-only.
- Confirm which seeded blueprint issue groups already overlap with open GitHub issues and which need new seeding.
