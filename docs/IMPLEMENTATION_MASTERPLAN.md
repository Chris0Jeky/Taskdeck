# Taskdeck Implementation Masterplan

Last Updated: 2026-02-16  
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
- MVP should include a dogfooding workflow: paste structured plan text in chat and bootstrap a board/project from approved proposals.
- UX investments should be modular and reusable (keyboard-first, discoverable selectors, shared input-assist patterns).

## Current Cycle Outcome (Completed)

Delivered in the latest cycle:
1. Backend advanced slices completed: automation proposals/executor, archive recovery, chat, ops/logs, workers/health.
2. Frontend advanced views integrated: automations/chat/ops/archive and supporting APIs/types.
3. Maintainability refactor delivered (PR #23):
   - backend shared error contracts/mapping and authenticated-user controller base
   - frontend shared query-string and error-message utilities
4. CI hardening follow-up delivered:
   - workflow concurrency cancellation
   - frontend typecheck/build parity in CI
   - NuGet/Playwright caching and richer failure artifacts (TRX/JUnit uploads)
5. Mechanical invariants delivered:
   - docs governance CI checks (`scripts/check-docs-governance.mjs`, `scripts/check-github-ops-governance.mjs`)
   - architecture boundary test project (`Taskdeck.Architecture.Tests`)
6. Security/observability slice delivered:
   - boards controller family retrofitted to claims-first authz
   - API authz harness helpers for 401/403/cross-user assertions
   - request correlation middleware + Ops CLI correlation propagation
   - timing/result diagnostics for log query and automation execution paths
7. Test surface expanded and verified:
   - Backend: 461 passing
   - Frontend unit: 245 passing
   - E2E: 11 passing
8. Documentation consolidation retained:
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

1. P0: Claims-first identity retrofit across remaining controller families: columns/cards/labels/export/audit/queue/board-access/users.
2. P0: Auth regression integration suite expansion for legacy + advanced controllers.
3. P0: Archive board lifecycle coherence (archive/unarchive visibility, restore semantics, UX parity).
4. P1: Interaction-mode guardrails to prevent drag side effects while editing card/task content.
5. P1: Command palette keyboard selection/activation model.
6. P1: Activity selector UX for board/entity/user discovery + easy ID reveal/copy affordance.
7. P1: Ops/automation contextual autocomplete + option scaffolding via shared input-assist module.
8. P1: Real LLM provider abstraction and environment-safe provider selection.
9. P1: Planner schema expansion with deterministic validation and stronger tests.
10. P1: Automation executor hardening (failure semantics, audit attribution, operation coverage).
11. P1: MVP dogfooding flow - parse pasted project checklist text in chat and generate board/bootstrap proposals for one-click setup.
12. P2: Refactoring/modularization sprint for maintainability and duplication reduction.
13. P2: Database-level export/import implementation.
14. P2: Log query scalability improvements and nullable-warning debt reduction.
15. P2: Deeper E2E expansion for keyboard flows, archive edge paths, and automation/ops error paths.
16. P1: One-click package framework for reusable board starter states (labels, columns, templates, and seeded cards).
17. P1: Starter pack catalog UX with preview/apply flow and per-pack conflict handling.
18. P1: Test fixture packs derived from the same package manifests for deterministic QA/E2E setup.
19. P2: Domain packs for common workflows (engineering, support, incidents, product, content, operations).
20. P2: Package versioning and migration strategy for existing boards.
21. P2: User-defined package export/import and organization-level shared package libraries.


## Prepackaged Starter States Track (Roadmap Additions)

Goal:
- reduce setup friction and make boards immediately useful
- make QA/E2E environments reproducible through deterministic starter states
- reuse one package definition across product onboarding, demos, and tests

Brainstormed package candidates (to be converted into scoped work items):
- Label packages: software delivery, bug triage, incident severity, customer support, product discovery, content production, compliance/risk.
- Column packages: simple Kanban, Scrum sprint, intake-triage-doing-done, incident command flow, support SLA flow, release train flow.
- Board blueprint packages: sprint board, roadmap board, on-call board, support queue, launch checklist board, design review board.
- Card template packages: bug report, incident ticket, feature request, technical debt, postmortem, release task, QA test case.
- Checklist template packages: DoR/DoD, pre-release checklist, rollback checklist, incident response checklist.
- Automation preset packages: stale-card nudges, due-date reminders, WIP breach alerts, auto-labeling suggestions, proposal gating defaults.
- Ops preset packages: common command templates, log query presets, correlation-ID trace bundles.
- Saved filter/view packages: blocked-only view, due-this-week view, critical-label view, owner-centric view.
- Permissions/access packages: default board role policies and invite presets for common team topologies.
- Seed-data demo packages: realistic sample boards/cards for walkthroughs and onboarding.
- Deterministic QA fixture packages: minimal/small/large datasets with stable IDs and timestamps.
- Edge-case fixture packages: blocked-card-heavy boards, overdue-heavy boards, archive-heavy boards, WIP-limit stress boards.
- Security fixture packages: unauthorized/forbidden/cross-user scenario seeds for auth contract validation.
- Performance fixture packages: high-card/high-column/high-label board seeds for load and latency profiling.
- Archive lifecycle packages: pre-seeded archive/restore scenarios for board/entity recovery testing.
- Activity discoverability packages: seeded histories across board/entity/user to validate selector UX.
- Keyboard workflow packages: board states designed to validate no-mouse task creation/edit/navigation paths.
- LLM/automation sandbox packages: curated prompts + expected proposal shapes for regression validation.
- Chat-to-project bootstrap packages: paste Markdown checklist/project plan and generate columns/cards/labels/proposals from it.
- Domain-specific packs: engineering backlog, agency workflow, content calendar, CRM-lite pipeline, research planning.
- "Golden path" E2E packs: canonical start states for smoke, regression, and release-candidate test suites.

Initial implementation shape:
1. Define a versioned package manifest schema (labels, columns, cards, automations, metadata, compatibility rules).
2. Build idempotent package-apply backend endpoints with dry-run and conflict reporting.
3. Add frontend package catalog with search, preview, and one-click apply.
4. Ship first-party packs: common labels + common column flows + 3-5 board blueprints.
5. Reuse package manifests to generate deterministic E2E/QA fixtures.
6. Add pack telemetry to measure adoption, setup-time reduction, and failure points.
7. Add pack migration/version compatibility checks for long-lived boards.
8. Add checklist-ingestion path for chat so pasted plans can map to pack templates and board bootstrap proposals.
## Next Best Steps (Immediate)

1. Continue claims-first retrofit for remaining legacy controller families and align response contracts.
2. Expand unauthorized/forbidden/cross-user matrix tests for both legacy and advanced routes.
3. Ship command palette keyboard navigation and add corresponding unit/E2E coverage.
4. Define and implement archive board lifecycle behavior contract (API + UI).
5. Start shared selector/input-assist infrastructure for Activity, Ops, and Automation forms.
6. Draft package-manifest RFC and ship first two starter packs (common labels + common column flows).
7. Define and implement MVP chat-to-project bootstrap acceptance flow (paste checklist -> approve proposals -> populated board).

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
