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
- Security and identity convergence remains the highest-priority engineering track.
- Automation remains proposal-first and review-first by default.
- UX investments should be modular and reusable (keyboard-first, discoverable selectors, shared input-assist patterns).

## Current Cycle Outcome (Completed)

Delivered in the latest cycle:
1. Backend advanced slices completed: automation proposals/executor, archive recovery, chat, ops/logs, workers/health.
2. Frontend advanced views integrated: automations/chat/ops/archive and supporting APIs/types.
3. Maintainability refactor delivered (PR #23):
   - backend shared error contracts/mapping and authenticated-user controller base
   - frontend shared query-string and error-message utilities
4. Test surface expanded and verified:
   - Backend: 459 passing
   - Frontend unit: 245 passing
   - E2E: 11 passing
5. Documentation consolidation retained:
   - active docs remain focused at `docs/` root
   - detail packs/audits archived under `docs/archive/2026-02-13_phase4-doc-consolidation/`

## Roadmap by Horizon

### Horizon A (Week 1 to 2): Security and Identity Convergence

Focus:
- enforce `[Authorize]` and claim-derived identity on legacy controller families
- remove query/body actor identity where claims should be source of truth
- align all controller failure responses with shared error contract patterns
- add integration coverage for unauthorized/forbidden/cross-user paths

Exit Criteria:
- no production endpoint depends on caller-supplied actor IDs for identity
- core + advanced controllers have consistent auth behavior
- security failures expose stable, documented response shapes

### Horizon B (Week 3 to 6): Automation Hardening and Provider Strategy

Focus:
- add production-capable LLM provider path behind config/feature gates
- expand planner operation extraction in a structured, test-backed way
- harden executor behavior for partial failure semantics and audit quality
- improve archive and automation coherence for board-level restore/execution workflows

Exit Criteria:
- provider strategy supports safe mock/prod switching
- planner/executor coverage materially expanded with explicit safety constraints
- archive + automation workflows are behaviorally consistent in UI/API

### Horizon C (Week 7 to 12): UX and Operability Hardening

Focus:
- command palette keyboard-first item navigation and activation
- activity view discoverability via selectors/autocomplete instead of raw ID-only flow
- ops/automation input ergonomics via modular autocomplete/option generation
- drag/edit interaction conflict hardening and escape-driven navigation ergonomics
- sticky/always-reachable shortcuts/help affordance in workspace shell

Exit Criteria:
- key operations can be completed keyboard-first in shell-level and ops flows
- ID-heavy workflows are replaced or assisted by discoverable selectors
- drag/edit and escape interaction regressions are resolved and test-backed
- shared input-assist and navigation patterns are reusable across feature modules

## Active Backlog (Prioritized)

1. P0: Claims-first identity retrofit across boards/columns/cards/labels/export/audit/queue/board-access/users.
2. P0: Auth regression integration suite expansion for legacy + advanced controllers.
3. P0: Archive board lifecycle coherence (archive/unarchive visibility, restore semantics, UX parity).
4. P1: Interaction-mode guardrails to prevent drag side effects while editing card/task content.
5. P1: Command palette keyboard selection/activation model.
6. P1: Activity selector UX for board/entity/user discovery + easy ID reveal/copy affordance.
7. P1: Ops/automation contextual autocomplete + option scaffolding via shared input-assist module.
8. P1: Real LLM provider abstraction and environment-safe provider selection.
9. P1: Planner schema expansion with deterministic validation and stronger tests.
10. P1: Automation executor hardening (failure semantics, audit attribution, operation coverage).
11. P2: Refactoring/modularization sprint for maintainability and duplication reduction.
12. P2: Database-level export/import implementation.
13. P2: Log query scalability improvements and nullable-warning debt reduction.
14. P2: Deeper E2E expansion for keyboard flows, archive edge paths, and automation/ops error paths.

## Next Best Steps (Immediate)

1. Complete claims-first retrofit for core board controllers and align response contracts.
2. Add unauthorized/forbidden matrix tests for both legacy and advanced routes.
3. Ship command palette keyboard navigation and add corresponding unit/E2E coverage.
4. Define and implement archive board lifecycle behavior contract (API + UI).
5. Start shared selector/input-assist infrastructure for Activity, Ops, and Automation forms.

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
  - Mitigation: shared modular patterns (selectors/input-assist/navigation) + RFC-first implementation
- Risk: docs drift returns after consolidation
  - Mitigation: strict update requirements on behavior-changing PRs