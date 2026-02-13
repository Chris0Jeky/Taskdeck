# Taskdeck Implementation Masterplan

Last Updated: 2026-02-13  
Planning Horizon: Next 8 to 12 weeks  
Companion Status Doc: `docs/STATUS.md`

## Purpose

This is the active execution guide for sequencing implementation.
Update this file at the end of each meaningful delivery cycle.

## Planning Principles

- `docs/STATUS.md` is authoritative for current shipped reality.
- Prefer finishing cross-cutting consistency work before adding new surface area.
- Security and identity convergence is the current highest-priority engineering track.
- Automation remains proposal-first and review-first by default.
- UX investments should improve operability in a modular way (keyboard-first, discoverable inputs, lower manual ID friction).

## Current Cycle Outcome (Completed)

Delivered in the latest cycle:
1. Backend advanced slices completed: automation proposals/executor, archive recovery, chat, ops/logs, workers/health.
2. Frontend advanced views integrated: automations/chat/ops/archive and supporting APIs/types.
3. Test surface expanded and verified:
   - Backend: 439 passing
   - Frontend unit: 238 passing
   - E2E: 11 passing
4. Documentation consolidation completed:
   - active docs reduced to operational set
   - non-authoritative packs archived under `docs/archive/2026-02-13_phase4-doc-consolidation/`

## Roadmap by Horizon

### Horizon A (Week 1 to 2): Security and Identity Convergence

Focus:
- enforce `[Authorize]` and claim-derived identity on legacy controller families
- remove query/body actor identity where claims should be source of truth
- add integration coverage for unauthorized/forbidden/cross-user paths

Exit Criteria:
- no production endpoint depends on caller-supplied actor IDs for identity
- core + advanced controllers have consistent auth behavior

### Horizon B (Week 3 to 6): Automation Hardening and Provider Strategy

Focus:
- add production-capable LLM provider path behind config/feature gates
- expand planner operation extraction in a structured, test-backed way
- harden executor behavior for partial failure semantics and audit quality
- improve archive and automation coherence for board-level restore/execution workflows

Exit Criteria:
- provider strategy supports safe mock/prod switching
- planner/executor coverage materially expanded with clear safety constraints
- audit trail quality improves for automation-originated mutations

### Horizon C (Week 7 to 12): UX and Operability Hardening

Focus:
- command palette keyboard-first item navigation and activation
- activity view discoverability (pickers/autocomplete instead of manual ID-only flow)
- ops/automation input ergonomics via contextual option generation/autocomplete
- drag/edit interaction conflict hardening and escape-driven navigation ergonomics
- modular refactoring pass across high-churn frontend/backend areas to reduce duplication

Exit Criteria:
- key operations can be completed keyboard-first in shell-level and ops flows
- ID-heavy workflows are replaced or assisted by discoverable selectors
- interaction regressions from drag/edit/escape overlap are resolved and test-backed
- refactor pass lands with no regression in current test baseline

## Active Backlog (Prioritized)

1. P0: Claims-first identity retrofit across boards/columns/cards/labels/export/audit/queue/board-access/users.
2. P0: Auth regression integration suite expansion for legacy + advanced controllers.
3. P1: Real LLM provider abstraction and environment-safe provider selection.
4. P1: Planner schema expansion with deterministic validation and stronger tests.
5. P1: Automation executor hardening (failure semantics, audit attribution, operation coverage).
6. P1: Archive workflow coherence for board archive/restore behavior.
7. P1: Command palette keyboard selection and activation model.
8. P1: Activity entity/user/board selector UX with discoverability support.
9. P2: Ops/automation form autocomplete and contextual options.
10. P2: Refactoring/modularization sprint for maintainability and duplication reduction.
11. P2: Database-level export/import implementation.
12. P2: Log query scalability improvements and nullable-warning debt reduction.

## Next Best Steps (Immediate)

1. Ship auth/claims retrofit for core board controllers first.
2. Add matching API integration tests for unauthorized/forbidden matrix.
3. Define UX RFC for command palette keyboard model + activity selectors.
4. Implement board archive/restore coherence fix path and add regression checks.
5. Start modular refactor inventory (frontend shell + automation/ops services + shared validation).

## Documentation Operating Model

Active docs:
- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/TESTING_GUIDE.md`
- `docs/MANUAL_TEST_CHECKLIST.md`

Archived docs:
- all superseded detail packs and historical snapshots under `docs/archive/`

Rule:
- Any behavior-changing PR must update status + masterplan and relevant testing/checklist docs.

## Weekly Cadence

- Start of week:
  - reconcile `docs/STATUS.md`
  - commit top 3 backlog items for the week
- During week:
  - ship tested vertical slices
  - avoid adding new top-level planning docs
- End of week:
  - update this file with completed work and reprioritized next steps

## Risk Register

- Risk: auth retrofit causes regressions in existing UI flows
  - Mitigation: staged rollout + integration contract tests
- Risk: automation parser/executor changes introduce unsafe operations
  - Mitigation: strict schema validation + proposal-first enforcement
- Risk: UX changes increase complexity without cohesion
  - Mitigation: RFC-first design and shared component/pattern reuse
- Risk: docs drift returns after consolidation
  - Mitigation: strict update requirements on behavior-changing PRs
