# Taskdeck Implementation Masterplan

Last Updated: 2026-02-11
Planning Horizon: Next 8 to 12 weeks
Companion Status Doc: `docs/STATUS.md`

## Purpose

This plan is the active execution guide for implementation decisions, priorities, and sequencing.
It is designed to prevent context loss across future coding sessions.

## Planning Principles

- Keep `docs/STATUS.md` authoritative for reality.
- Prioritize test-backed delivery over feature volume.
- Finish incomplete UX behavior before adding broad new surface area.
- Maintain Clean Architecture boundaries.

## Outcome Targets (12 Weeks)

1. Stable quality gates (unit + integration + E2E smoke in CI)
2. Phase 3 completion to 100 percent
3. Phase 4 progress on one major feature track (CLI or time tracking)
4. Reduced documentation drift via one status file + archive discipline

## Roadmap by Horizon

### Horizon A (Week 1 to 2): Stabilize and Close Gaps

- Finish keyboard shortcut TODO handlers in `BoardView`
- Add backend API integration test project (`Taskdeck.Api.Tests`) with core CRUD + WIP scenarios
- Add minimal E2E smoke suite for critical flow
  - create board
  - create column and card
  - move card respecting WIP

Exit Criteria:
- all current tests passing
- API integration tests introduced and green
- at least 2 E2E smoke tests green locally

### Horizon B (Week 3 to 6): Reliability and Developer Workflow

- Wire CI pipeline for backend unit, frontend unit, API integration, E2E smoke
- Add coverage reporting artifacts
- Add quality gates to block merges on failed suites
- Address any flaky tests and stabilize deterministic fixtures

Exit Criteria:
- CI green on main branch
- repeatable local and CI runs
- no unresolved flaky tests in active suites

### Horizon C (Week 7 to 12): Phase 4 Feature Progress

Choose one primary track and complete first slice end-to-end.

Track Option 1: CLI client (recommended first for fast value)
- Add `Taskdeck.Cli` project
- Implement board/card list, quick card create, move card command
- Reuse application services and shared DTO contracts

Track Option 2: Time tracking
- Extend domain model for time entries
- Add API endpoints and UI controls
- Add minimal reporting summary

Exit Criteria:
- one Phase 4 track available behind stable tests
- documented usage and migration notes

## Active Backlog (Prioritized)

1. P0: Keyboard shortcut TODO completion in `frontend/taskdeck-web/src/views/BoardView.vue`
2. P0: Add `backend/tests/Taskdeck.Api.Tests` integration coverage
3. P0: Add frontend E2E smoke tests in `frontend/taskdeck-web`
4. P1: CI orchestration for all active suites
5. P1: Baseline browser mapping dependency refresh warning cleanup
6. P2: Start CLI track or time-tracking track

## Next Best Steps (Current Sprint)

These are the immediate recommended actions and are now part of this masterplan:

1. Implement keyboard shortcut open/create actions to remove Phase 3 partial status.
2. Create backend API integration tests for boards, cards, and WIP-limit error paths.
3. Add a minimal Playwright E2E smoke layer for end-to-end confidence.
4. Configure CI to run backend unit + frontend unit + API integration + E2E smoke.
5. Select and start one primary Phase 4 track (CLI preferred for quickest utility).

## Weekly Operating Cadence

- Start of week:
  - refresh `docs/STATUS.md` if implementation reality changed
  - confirm top 3 backlog priorities
- During week:
  - feature delivery with tests in same change set
  - keep archive-only docs untouched unless explicitly referenced
- End of week:
  - update this file with completed milestones and next sprint priorities

## Risk Register

- Risk: documentation drift reappears
  - Mitigation: update only `STATUS.md` for status; archive all narrative notes
- Risk: integration/E2E adoption slows feature velocity
  - Mitigation: keep smoke scope intentionally small first
- Risk: Phase 4 sprawl without finishing one slice
  - Mitigation: one-track-at-a-time rule for Horizon C
