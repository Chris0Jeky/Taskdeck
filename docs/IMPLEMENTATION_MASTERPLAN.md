# Taskdeck Implementation Masterplan

Last Updated: 2026-02-11  
Planning Horizon: Next 8 to 12 weeks  
Companion Status Doc: `docs/STATUS.md`

## Purpose

This is the active execution guide for implementation sequencing.
It should be updated at the end of each meaningful implementation cycle.

## Planning Principles

- Keep `docs/STATUS.md` authoritative for reality and test numbers.
- Ship only with tests for changed behavior.
- Grow Phase 4 in one primary track at a time.
- Keep CI gates aligned with local commands.

## Current Sprint Outcome (Completed)

All requested items were implemented:

1. Keyboard shortcut TODO handlers finished in `frontend/taskdeck-web/src/views/BoardView.vue`.
2. Backend API integration tests added in `backend/tests/Taskdeck.Api.Tests`.
3. Playwright E2E smoke tests added in `frontend/taskdeck-web/tests/e2e/smoke.spec.ts`.
4. CI gates added in `.github/workflows/ci.yml` for backend, frontend, and E2E smoke.
5. Phase 4 CLI track started via `backend/src/Taskdeck.Cli`.

## Roadmap by Horizon

### Horizon A (Week 1 to 2): Consolidate New Quality Gates

- Validate first CI runs and fix cross-platform failures quickly.
- Expand API integration test suite beyond current smoke/business-rule baseline.
- Expand E2E smoke suite to include:
  - card move between columns
  - WIP-limit rejection path
  - board settings update/delete flow

Exit Criteria:
- CI stable on main.
- API integration tests cover core CRUD + error paths.
- E2E smoke includes at least 5 stable, critical flows.

### Horizon B (Week 3 to 6): Deepen Reliability and Coverage

- Add coverage artifacts and optional thresholds in CI.
- Harden flaky areas (test data isolation, deterministic dates, retry strategy).
- Address dependency/tooling warnings (e.g., baseline browser mapping freshness).

Exit Criteria:
- repeatable local and CI runs with low flake rate
- clear quality dashboard of unit/integration/E2E status

### Horizon C (Week 7 to 12): Phase 4 Feature Delivery

Primary Track (already started): CLI

- Expand CLI commands:
  - boards update/delete/archive
  - columns create/update/delete/reorder
  - cards list/update/delete/search
  - labels list/create/update/delete
- Improve CLI UX:
  - consistent output formatting
  - clear error messages and exit codes
  - help for each subcommand

Secondary Track (after CLI milestone): time tracking

Exit Criteria:
- CLI provides end-to-end utility for daily task operations.
- CLI behavior is covered by focused unit/integration tests.

## Active Backlog (Prioritized)

1. P0: Expand API integration tests for full controller surface and error mapping.
2. P0: Expand Playwright smoke tests to cover drag-and-drop and WIP-limit failure.
3. P0: Verify and stabilize CI on first runs; fix platform/environment drift.
4. P1: CLI command surface expansion and structured output.
5. P1: baseline-browser-mapping warning cleanup.
6. P2: Start time-tracking design spike after CLI milestone.

## Next Best Steps (Updated)

1. Add at least 3 new API integration tests:
   - labels CRUD happy path
   - card update/delete paths
   - not-found and validation error response mapping
2. Add at least 3 new E2E tests:
   - move card between columns
   - WIP limit enforcement message
   - board settings rename/archive flow
3. Add first CLI follow-up commands:
   - `boards update`
   - `columns create`
   - `cards list`
4. Run CI workflow after merge and close any platform-specific failures.

## Weekly Cadence

- Start of week:
  - sync `docs/STATUS.md` with repo reality
  - pick top 3 backlog items
- During week:
  - ship in vertical slices with tests
  - avoid creating new top-level planning docs
- End of week:
  - update this file with completed work and next-best-step reorder

## Risk Register

- Risk: CI instability after introducing multi-stack jobs
  - Mitigation: keep test setup deterministic; isolate DB/test artifacts
- Risk: Phase 4 scope sprawl
  - Mitigation: finish CLI milestone before opening another major track
- Risk: Documentation drift
  - Mitigation: only `STATUS.md` and this file are authoritative
