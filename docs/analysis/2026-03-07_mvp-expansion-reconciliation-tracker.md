# MVP Expansion Reconciliation Tracker

Date: 2026-03-07
Branch: `docs/mvp-expansion-reconciliation`
Status: In progress

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

- Create branch and tracker.
- Read source-of-truth docs and MVP expansion packs.
- Build current-doc gap map.
- Build GitHub issue overlap map.

### Batch 2

- Update canonical planning docs:
  - `docs/IMPLEMENTATION_MASTERPLAN.md`
  - `docs/STATUS.md`
  - `docs/INDEX.md`
- Add or promote a clearer novice-first documentation entry point if justified.

### Batch 3

- Update user-facing/product docs:
  - `docs/USER_MANUAL.md`
  - `docs/DOGFOODING_GUIDE.md`
  - `docs/SCENARIOS.md`
  - `docs/TESTING_GUIDE.md`
- Add reconciliation notes for issue planning and future batch execution.

## Current Findings

### Documentation gaps

- Active docs describe the product mostly through current surfaces (`Boards`, `Inbox`, `Automations`, `Ops`) rather than a novice-first `Home -> Inbox/Review -> Projects` journey.
- `USER_MANUAL.md` and `DOGFOODING_GUIDE.md` still normalize raw `Board ID` usage for queue flows.
- There is no canonical `Start Here` / first-15-minutes doc bridging repo setup, product thesis, seeded demo flow, and daily-use golden path.
- `INDEX.md` is maintainers-strong but not yet oriented around audience entry points.

### GitHub issue overlap

- Existing overlap found:
  - `#96` onboarding/contextual help
  - `#93` global search and quick actions
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

## Verification Log

- pending

## Next Actions

1. Finish the current-doc gap map with explicit file-level recommendations.
2. Capture the MVP expansion roadmap inside `IMPLEMENTATION_MASTERPLAN.md` without overstating shipped reality.
3. Add a clearer documentation entry point for the novice-first product story.
