# Taskdeck Implementation Masterplan

Last Updated: 2026-02-20  
Planning Horizon: Next 8 to 12 weeks  
Companion Active Docs:
- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/TESTING_GUIDE.md`
- `docs/MANUAL_TEST_CHECKLIST.md`

## Purpose

This is the active execution guide for sequencing implementation.
Update this file at the end of each meaningful delivery cycle.

## Planning Principles

- `docs/STATUS.md` is authoritative for current shipped reality.
- Prefer finishing cross-cutting consistency work before adding new surface area.
- Security and identity convergence remains the highest-priority engineering track.
- Cross-user existence policy is fixed: return `403` for authenticated-but-unauthorized access and `404` for true missing resources.
- Automation remains proposal-first and review-first by default.
- MVP should include a dogfooding workflow: paste structured plan text in chat and bootstrap a board/project from approved proposals.
- UX investments should be modular and reusable (keyboard-first, discoverable selectors, shared input-assist patterns).
- Every issue must carry exactly one priority label (`Priority I` through `Priority V`).
- Out-of-code and configuration work (containerization, deployment, security posture, observability, DR) must be tracked as first-class backlog items.

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
   - Backend: 566 passing
   - Frontend unit: 281 passing
   - E2E: 14 passing
8. Documentation consolidation retained:
   - active docs remain focused at `docs/` root
   - detail packs/audits archived under `docs/archive/2026-02-13_phase4-doc-consolidation/`
9. Stage 0 governance follow-through:
   - active docs cross-link/date-stamp freeze completed for canonical docs
   - project safety view standardized as `No Status` (`no:status`)
   - weekly backlog seeding cadence and RC hard-gate policy documented in active ops docs
10. Security convergence progress:
   - `[Authorize]` enforced across remaining legacy controller families
   - claims-first identity retrofit delivered for columns/cards/labels/export/queue/board-access
   - caller-supplied actor query/body IDs removed from those controller families
   - API integration suite expanded for legacy unauthorized/forbidden/cross-user regression checks
   - API integration suite expanded for legacy unauthorized-path regression checks
11. Frontend runtime alignment:
   - CI and local developer baseline pinned to Node 24.13.1 (LTS) to match Vite 7 engine constraints
12. Security convergence completion for remaining legacy families:
   - audit controller now derives actor identity from claims for user-history and board-history access checks
   - users controller now enforces self-scope for read/update/activate/deactivate profile actions
   - audit frontend flow moved from user-id route calls to `/audit/users/me`
13. SEC-03 regression matrix delivery:
   - added explicit API integration matrix assertions for protected legacy + advanced routes
   - expanded policy coverage for `401` unauthenticated, `403` cross-user unauthorized, and `404` true missing resources
14. SEC-04 API error-contract assertions delivery:
   - middleware-level JWT challenge/forbidden responses now emit stable `ApiErrorResponse` payloads
   - API integration assertions now explicitly enforce auth and validation error-contract shape stability
15. UX-01 archive lifecycle coherence delivery:
   - board settings archive action now reflects soft-delete semantics (reversible archive, not permanent deletion)
   - archive workspace now surfaces archived boards and supports restore via board lifecycle API flow
   - API integration roundtrip coverage added for archive-to-restore board lifecycle behavior
16. UX-02 drag/edit interaction safety guardrails delivery:
   - card and column drag now requires explicit drag handles
   - non-handle drag gestures are ignored to prevent accidental movement during adjacent edit interactions
   - frontend unit + E2E coverage added for handle-only drag behavior and conflict paths
17. UX-03 command palette keyboard model delivery:
   - command palette now supports keyboard-first filtering, item selection, and activation
   - shell interactions preserve deterministic close behavior (`Escape`) and focus handling
   - frontend unit + E2E coverage added for command palette keyboard navigation and activation
18. UX-04 activity selector discoverability delivery:
   - activity workflows now prioritize selector-first board/entity/user discovery instead of raw ID-first entry
   - board/entity selection now includes discoverable context and ID reveal/copy affordance
   - frontend unit + E2E coverage added for selector-based activity navigation and fetch flows
19. UX-04 shared input-assist scaffolding delivery:
   - shared input-assist combobox/listbox component added for reusable suggestion and keyboard-selection behavior
   - ops CLI template selection now uses input-assist with discoverable template metadata
   - automation chat board targeting now uses input-assist board suggestions with keyboard-first interactions
20. UX-05 escape behavior contract delivery:
   - workspace and board escape handling now follows a top-surface-first contract via shared escape-stack handling
   - board routes now exit to `/workspace/boards` when no transient surface is open
   - unit + E2E regression coverage validates escape ordering and board-exit behavior
21. AUTO-01 real-provider strategy delivery:
   - `ILlmProvider` selection now follows deterministic environment-aware policy evaluation (`Mock` vs `OpenAI`)
   - live provider usage is explicitly gated by config (`EnableLiveProviders`, provider mode, development override guard)
   - OpenAI provider path and policy constraints are test-backed while preserving proposal-first chat flow semantics
22. AUTO-02 planner/executor hardening delivery:
   - planner instruction coverage now includes deterministic board/column intents (rename/archive/unarchive/reorder) with explicit board/position validation
   - executor operation parameter parsing now fails with deterministic validation errors instead of exception-driven fallbacks
   - partial-failure behavior is test-backed as transactional rollback + proposal failure status update with actionable operation-sequenced reasoning and improved audit entity attribution
23. MVP-01 chat-to-project bootstrap delivery:
   - chat now supports canonical Markdown checklist ingestion and proposal-first bootstrap operation generation for board-scoped sessions
   - proposal review remains mandatory, with chat exposing one-click approve + execute action for generated checklist bootstrap proposals
   - backend + API + frontend tests cover canonical happy path and key checklist parse/validation failures
24. PACK-01 starter-pack manifest foundation delivery:
   - added a versioned starter-pack manifest contract (`schemaVersion` `1.0`) for labels, columns, templates, and seed cards
   - added deterministic backend parsing/validation service with explicit compatibility and cross-reference constraints
   - added dedicated application tests covering canonical success + key parse/validation failure paths
25. PACK-01 null-collection hardening follow-up:
   - manifest validation now handles explicit JSON `null` collections deterministically (array-shape errors instead of null-reference exceptions)
   - nested collection paths (`compatibility.requiredFeatures`, template checklists, seed-card labels) are now null-safe and regression-tested
26. PACK-02 starter-pack apply backend delivery:
   - added authenticated board-scoped apply endpoint: `POST /api/boards/{boardId}/starter-packs/apply`
   - delivered idempotent apply semantics with dry-run actionable conflict reporting for labels/columns/seed-card references
   - added API integration coverage for apply success, re-apply idempotency, dry-run conflict report, and non-dry-run conflict response
27. PACK-03 starter-pack frontend catalog delivery:
   - added board-level starter pack catalog UI with search/filter and manifest preview details
   - integrated dry-run preview and one-click apply flow against the backend apply endpoint
   - added frontend API + component interaction tests for preview/apply/conflict/empty states

## Roadmap by Horizon

### Horizon A (Week 1 to 2): Security and Identity Convergence

Focus:
- enforce `[Authorize]` and claim-derived identity on legacy controller families
- remove query/body actor identity where claims should be source of truth
- align all controller failure responses with shared error contract patterns
- enforce the `401/403/404` contract (`401` unauthenticated, `403` authenticated-but-unauthorized/cross-user, `404` true missing)
- add integration coverage for unauthorized/forbidden/cross-user paths

Exit Criteria:
- no production endpoint depends on caller-supplied actor IDs for identity
- core + advanced controllers have consistent auth behavior
- security failures expose stable, documented response shapes

### Horizon B (Week 3 to 6): Automation Hardening and Provider Strategy

Focus:
- operationalize real-provider usage with deterministic policy gates and safe defaults across environments
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

### Horizon D (Post-Phase-4): Platform, Deployment, and Operability Baseline

Focus:
- containerized runtime baseline with reverse-proxy and compression posture
- observability baseline (metrics/traces/log correlation + alerts)
- performance and concurrency budgets with repeatable harnesses
- production data/runtime posture decisions (DB provider migration strategy, caching strategy)
- disaster-recovery and staged rollout operational readiness

Exit Criteria:
- environment bring-up and rollout paths are documented, test-backed, and repeatable
- core SRE signals exist for errors, latency, backlog, worker health, and cost drift
- release governance includes provenance/compliance artifacts (SBOM and documented rollback)

### Horizon E (Post-Phase-4): Collaboration, Integrations, and Product Maturity

Focus:
- realtime collaboration and notification ecosystem
- integrations/webhooks/connectors foundation
- analytics and planning surfaces
- compliance/security expansion (SSO/MFA, data portability, dependency-security policy)
- UX maturity (accessibility, search, onboarding, offline readiness)

Exit Criteria:
- collaboration and integration foundations are production-safe and test-backed
- growth-oriented UX and analytics features remain consistent with security and operability controls

## Active Backlog (Priority-Labeled)

### Priority I (Current Phase 4 Completion Path)

- Security and policy convergence: `#33`, `#34`, `#44`
- Starter packs foundation: `#48`, `#49`, `#50`, `#51`
- Tech-debt blockers for stable expansion: `#52`, `#53`, `#54`

### Priority II (Immediate Post-Phase-4 Foundation)

- Real-time and observability baseline: `#67`, `#68`
- Container/deployment and performance harness baseline: `#69`, `#70`
- Multi-tenancy strategy and collaboration/integration foundations: `#71`, `#72`, `#73`, `#74`, `#75`, `#76`

### Priority III (Expansion Tranche: Analytics, Security, Compliance)

- Analytics and forecasting: `#77`, `#78`, `#79`
- Security/compliance expansion: `#80`, `#81`, `#82`, `#83`, `#106`, `#110`

### Priority IV (Expansion Tranche: Platform, Test, UX, Docs Maturity)

- Platform and ops maturity: `#84`, `#85`, `#86`, `#101`, `#102`, `#103`, `#104`, `#105`, `#111`
- Test maturity: `#87`, `#88`, `#89`, `#90`, `#91`
- UX and onboarding maturity: `#92`, `#93`, `#94`, `#95`, `#96`
- Developer/user docs maturity: `#99`, `#100`

### Priority V (Meta/Historical)

- Wave index and historical/closed tracking: `#107` and completed governance items.

## Research Reconciliation (WIP PDFs, Feb 2026)

Research sources reviewed:
- `docs/WIP/FutureExpansionAndImprovementsChecklist.pdf`
- `docs/WIP/In-DepthAnalysisAndProgressReport(Feb2026).pdf`
- `docs/WIP/Scaling and Hardening Taskdeck (Vue 3 + ASP.NET Core) - Comprehensive Guide.pdf`

Strategic reconciliation applied:
- Keep current sequence: finish Phase 4 consistency/security first (`Priority I`) before broad feature expansion.
- Translate research recommendations into dependency-aware issues rather than broad unscoped themes.
- Treat non-code operations/configuration work as a mandatory delivery track, not "later ops".

## Out-of-Code and Configuration Coverage Matrix

Covered by seeded issues:
- Docker + reverse proxy + compression baseline: `#69`
- Staged rollout policy (blue/green/canary): `#101`
- IaC baseline: `#102`
- SBOM/release provenance: `#103`
- Cost guardrails: `#104`
- Backup/restore disaster recovery: `#86`
- OpenTelemetry metrics/tracing and alerting runbook: `#68`
- Load/concurrency harness and budgets: `#70`
- API abuse/rate limiting: `#81`
- OWASP/security headers and CSRF/XSS baseline: `#80`
- Dependency vulnerability management policy: `#106`
- Secrets/configuration management baseline: `#110`
- DB migration strategy and cache strategy: `#84`, `#85`
- Cloud target topology and autoscaling ADR: `#111`

Outstanding strategy-level gap to monitor:
- no major out-of-code categories from the reviewed WIP PDFs are currently untracked; residual risk is execution sequencing and closure quality.


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
3. Add frontend package catalog with search, preview, and one-click apply (delivered in PACK-03, issue #49).
4. Ship first-party packs: common labels + common column flows + 3-5 board blueprints.
5. Reuse package manifests to generate deterministic E2E/QA fixtures.
6. Add pack telemetry to measure adoption, setup-time reduction, and failure points.
7. Add pack migration/version compatibility checks for long-lived boards.
8. Add checklist-ingestion path for chat so pasted plans can map to pack templates and board bootstrap proposals.
## Next Best Steps (Immediate)

1. Maintain completed `Priority I` security/policy tranche (`#33`, `#34`, `#44`) with regression coverage while closing remaining auth/contract drift.
2. Complete remaining `Priority I` starter-pack foundation and debt blockers (`#50` to `#54`).
3. Promote Wave A foundation issues (`#67` to `#71`) to active execution only after `Priority I` is materially reduced.
4. Keep issue `#107` updated as the canonical expansion-wave index.
5. Maintain one-priority-label-per-issue discipline (`Priority I` to `Priority V`) and re-evaluate quarterly.

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
