# 2026-03-07 MVP Expansion Source Coverage Audit

Status: Non-authoritative audit record with seeded lower-priority follow-through captured.

Purpose:
- verify that every file under `docs/InReview/MVP_EXPANSION/` is either:
  - promoted into canonical docs,
  - converted into dependency-aware issue scope,
  - or explicitly deferred without being lost
- preserve the snippet-level and architecture-level guidance that was only partially captured by the first reconciliation pass

## Coverage Summary

- The near-horizon diagnosis from the `MINIMAL` pack is already promoted well:
  - product legibility before breadth
  - `Home` / `Review` / `Today`
  - board-centered follow-through
  - proposal readability
  - onboarding/help/docs/testing coherence
- The exact mode/navigation contracts and the backend aggregate-service direction from the `EXPANDED` pack are only partially preserved.
- The snippet assets were not lost, but several of their most useful implications were only implied in current docs/issues rather than carried explicitly.
- The secondary backlog from the full pack now has split treatment:
  - useful secondary follow-through is seeded as `#329` to `#334`
  - some details still sit on reuse anchors instead of dedicated issues
  - broader agent/knowledge/release-gate architecture is now seeded as `#335` to `#341`
- Wave naming note:
  - the source packs call the immediate novice-first productization tranche `Wave I`
  - canonical GitHub/docs indexing uses `Wave P` for that same tranche

## Source-to-Target Matrix

### MINIMAL Pack

| Source | Key signal | Current target(s) | Status | Carry-forward note |
|---|---|---|---|---|
| `docs/InReview/MVP_EXPANSION/MINIMAL/taskdeck_review_2026-03-06/00_READ_ME_FIRST.md` | demo infrastructure is ahead of self-serve product clarity | `README.md`, `docs/STATUS.md`, `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/analysis/2026-03-07_mvp-expansion-reconciliation-tracker.md`, `#318` to `#334` | promoted + seeded | in-app demo/self-serve affordances are now preserved in the lower-priority `#330` follow-through slice |
| `docs/InReview/MVP_EXPANSION/MINIMAL/taskdeck_review_2026-03-06/01_DEMO_COMPLETENESS_ASSESSMENT.md` | distinguish demo-as-proof from self-serve product onboarding | `docs/STATUS.md`, `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/DEMO_PLAYBOOK.md`, `docs/TESTING_GUIDE.md`, `#330` | partial + seeded | `Demo Tools`, guided narrative/demo-tour, and hero-board quality now have explicit issue coverage in `#330` |
| `docs/InReview/MVP_EXPANSION/MINIMAL/taskdeck_review_2026-03-06/02_GOLDEN_PATH_AND_PRODUCTIZATION.md` | surface taxonomy, start surface, board context travel, selectors, readable proposals, empty/help states | `docs/START_HERE.md`, `docs/USER_MANUAL.md`, `docs/STATUS.md`, `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/ISSUE_EXECUTION_GUIDE.md`, `#320`, `#322`, `#324`, `#326` | promoted + seeded | board-aware quick capture and exact CTA patterns should stay explicit in `#326` implementation |
| `docs/InReview/MVP_EXPANSION/MINIMAL/taskdeck_review_2026-03-06/03_DOGFOODING_AND_USEFUL_NOW.md` | honest useful-now posture, solo-dev dogfooding shape, friction metrics | `docs/DOGFOODING_GUIDE.md`, `docs/USER_MANUAL.md`, `README.md`, `#333` | partial + seeded | saved-views follow-through is now preserved in `#333`; exact board/column/label defaults and metric taxonomy still remain weakly represented |
| `docs/InReview/MVP_EXPANSION/MINIMAL/taskdeck_review_2026-03-06/04_SCENARIO_MATRIX_AND_TEST_PLAN.md` | tiered product smoke/scenario/live/adversarial testing strategy | `docs/TESTING_GUIDE.md`, `docs/SCENARIOS.md`, `#328`, `#331`, `#332` | partial + seeded | named scenario matrix, HTML report, assertions, presets, soak mode, replay-from-trace, and internal scenario composer are now preserved as explicit backlog items |
| `docs/InReview/MVP_EXPANSION/MINIMAL/taskdeck_review_2026-03-06/05_MANUAL_AND_DOCS_STRATEGY.md` | audience-layered docs set with `START_HERE` as bridge doc | `README.md`, `docs/START_HERE.md`, `docs/USER_MANUAL.md`, `docs/INDEX.md`, `#100` | promoted + seeded | screenshot/gif placement and fuller manual appendix structure remain later maturity work |
| `docs/InReview/MVP_EXPANSION/MINIMAL/taskdeck_review_2026-03-06/06_PRIORITIZED_BACKLOG.md` | source-pack Wave I ordering plus secondary demoability/harness/productivity/agent backlog | `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/ISSUE_EXECUTION_GUIDE.md`, `docs/TaskdeckNextWorkChecklist.md`, `#318` to `#341` | core promoted; later waves seeded | `Demo Tools`, guided narrative, nav badges, hero-board quality, HTML report, saved views, replay-from-trace, scenario composer, and the future agent/knowledge slices now have explicit later-wave issue coverage |
| `docs/InReview/MVP_EXPANSION/MINIMAL/taskdeck_review_2026-03-06/INDEX.md` | pack inventory only | `docs/analysis/2026-03-07_mvp-expansion-reconciliation-tracker.md`, this audit | accounted for | no extra action needed |
| `docs/InReview/MVP_EXPANSION/MINIMAL/taskdeck_review_2026-03-06/TASKDECK_REVIEW_MASTER.md` | compiled copy of the numbered review files | same union as rows above | accounted for | treat as convenience artifact, not a second canonical source |

### EXPANDED Pack

| Source | Key signal | Current target(s) | Status | Carry-forward note |
|---|---|---|---|---|
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/00_MASTER_BLUEPRINT.md` | one core system, three modes, sequence human product before autonomy breadth | `docs/STATUS.md`, `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/ISSUE_EXECUTION_GUIDE.md`, `#318` to `#328` | promoted | exact mode contract should stay explicit during Wave P implementation |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/01_PRODUCT_STRUCTURE_AND_POSITIONING.md` | concrete nav architecture, route taxonomy, novice vocabulary | `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/USER_MANUAL.md`, `#320`, `#322` | partial | exact route map and vocabulary table are not yet preserved in canonical docs |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/02_GOLDEN_PATHS_AND_UX_SHAPE.md` | page specs for `Home`, `Review`, `Today`, action states, no raw IDs | `docs/START_HERE.md`, `docs/TESTING_GUIDE.md`, `docs/IMPLEMENTATION_MASTERPLAN.md`, `#320`, `#322`, `#324`, `#326`, `#328` | promoted + seeded | plain-language top-box and page-spec details are only partially preserved |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/03_FRONTEND_PRODUCTIZATION_PLAN.md` | concrete frontend execution order and file-level plan | `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/TaskdeckNextWorkChecklist.md`, `#320`, `#322`, `#324`, `#326`, `#96`, `#93`, `#328` | partial | file-order execution, minimum polish bar, and explicit board-aware UI patterns should remain visible during implementation |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/04_BACKEND_AND_DOMAIN_EXPANSION_PLAN.md` | aggregate workspace APIs, user preferences, proposal summary service, agent/tool/policy direction | `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/ISSUE_EXECUTION_GUIDE.md`, `#320`, `#324`, `#326` | partial | `UserPreference`, aggregate API rule, `IProposalSummaryService`, and tool/policy abstractions remain shallowly represented |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/05_AGENT_WORKSPACE_ARCHITECTURE.md` | `AgentProfile`, `AgentRun`, `AgentRunEvent`, tool registry, policy evaluator, run views | `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/STATUS.md`, `docs/TaskdeckNextWorkChecklist.md`, `#335`, `#336`, `#337`, `#338` | partial + seeded | the concrete later-wave issue set now exists; canonical docs still intentionally keep this future-facing rather than pretending the surface is shipped |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/06_INTEGRATIONS_KNOWLEDGE_AND_AUTONOMY.md` | knowledge model, SQLite FTS, clipper/import connectors, integrations page, board assistant panels | `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/TaskdeckNextWorkChecklist.md`, `#335`, `#339`, `#340`, `#75`, `#98`, `#218`, `#219` | partial + seeded | the broader later-wave architecture is now tracked; assistant-panel and connector-detail design still intentionally stays future-facing |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/07_TESTING_METRICS_AND_OPERATIONS.md` | first-run smoke, telemetry/event taxonomy, launch gates, product-quality stack | `docs/TESTING_GUIDE.md`, `docs/IMPLEMENTATION_MASTERPLAN.md`, `#328`, `#77`, `#341` | partial + seeded | telemetry taxonomy and `R1` / `R2` / `R3` release framing now have explicit later-wave issue coverage |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/08_SEEDED_ISSUES_READY_TO_CREATE.md` | issue-ready decomposition of Epics A-E | `#318`, `#320`, `#322`, `#324`, `#326`, `#96`, `#100`, `#328`, `#335`, `#336`, `#337`, `#338`, `#339`, `#340`, `#341`, `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/ISSUE_EXECUTION_GUIDE.md` | seeded | later-wave EPIC C/D/E carry-forward is now explicitly issue-tracked |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/09_COMPREHENSIVE_MANUAL_BLUEPRINT.md` | manual structure aligned to top-level product navigation and in-app help mapping | `docs/USER_MANUAL.md`, `docs/START_HERE.md`, `docs/INDEX.md`, `#100` | partial | manual/help-center structure still reflects current workbench reality more than future shell/navigation |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/10_PHASED_ROADMAP_AND_RELEASE_PLAN.md` | phased sequence and `R1` / `R2` / `R3` release framing | `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/TESTING_GUIDE.md`, `#341` | promoted + seeded | canonical docs now preserve the release framing while `#341` tracks deeper telemetry and launch-gate follow-through |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/11_RISKS_NON_GOALS_AND_DECISION_RULES.md` | scope-control rules and anti-pattern guardrails | `docs/STATUS.md`, `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/ISSUE_EXECUTION_GUIDE.md`, `docs/GOLDEN_PRINCIPLES.md` | promoted | the decision-rule wording is now codified in the active docs/governance spine |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/INDEX.md` | pack read order and summary | `docs/analysis/2026-03-07_mvp-expansion-reconciliation-tracker.md`, this audit | accounted for | no extra action needed |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/TASKDECK_EXPANSION_MASTER.md` | compiled copy of the numbered blueprint files | same union as rows above | accounted for | treat as convenience artifact, not a second canonical source |

### Snippet Assets

| Source | Key signal | Current target(s) | Status | Carry-forward note |
|---|---|---|---|---|
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/snippets/backend/AgentProfile.cs` | board/workspace-scoped agent identity with template key and policy JSON | `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/TaskdeckNextWorkChecklist.md`, `#336` | partial + seeded | concrete entity/API follow-through now exists as later-wave issue scope |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/snippets/backend/AgentRun.cs` | inspectable run entity with explicit statuses and proposal linkage | `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/TaskdeckNextWorkChecklist.md`, `#336` | partial + seeded | run lifecycle is now explicitly carried by the seeded later-wave issue |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/snippets/backend/AgentRunService.cs` | agent runtime should gather context, evaluate policy, invoke tool registry, and create proposals | `docs/IMPLEMENTATION_MASTERPLAN.md`, `#337` | partial + seeded | policy/registry runtime abstractions now have explicit later-wave issue coverage |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/snippets/backend/AgentsController.cs` | first agent CRUD/manual-run API slice | `docs/IMPLEMENTATION_MASTERPLAN.md`, `#336` | partial + seeded | CRUD/manual-run API slice is now captured by the seeded later-wave issue |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/snippets/backend/HomeController.cs` | product-shaped aggregate endpoints belong under `/api/workspace/*` | `#320`, `#324`, `docs/IMPLEMENTATION_MASTERPLAN.md` | partial + seeded | aggregate-API rule should stay explicit during Wave P |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/snippets/frontend/HomeView.vue` | `Home` needs first-run state, urgent-work counts, recent boards, and clear CTAs | `#320`, `docs/IMPLEMENTATION_MASTERPLAN.md` | partial + seeded | CTA set and first-run state should not collapse into a generic dashboard |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/snippets/frontend/ProposalSummaryCard.vue` | proposal cards need plain-language summary, risk/source chips, affected entities, and direct actions | `#326`, `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/TESTING_GUIDE.md` | partial + seeded | backend summary service and entity/deep-link behavior should stay explicit |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/snippets/frontend/workspaceModeStore.ts` | workspace mode is first-class product state, not a temporary feature-flag proxy | `#320`, `docs/IMPLEMENTATION_MASTERPLAN.md` | partial + seeded | durable server-backed preference should remain in scope when practical |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/snippets/scenarios/novice-first-first-run.json` | concrete first-run acceptance scenario for `Home -> capture -> review -> board` | `docs/SCENARIOS.md`, `#328`, `docs/TESTING_GUIDE.md` | partial + seeded | scenario should guide the final first-run smoke instead of remaining only a note |

## Explicit Carry-Forward After Audit

### Wave P implementation details that should stay explicit

- `#320` should preserve:
  - durable workspace-mode behavior
  - product-shaped aggregate summary endpoints
  - `Home` as a real first-run surface, not a thin dashboard
- `#324` should preserve:
  - first-run onboarding checklist and project-creation flow
  - resumable/dismissible progression tied back to `Home`, `Review`, and board execution
- `#326` should preserve:
  - application-layer proposal summary generation
  - explicit board action rail behavior (`Capture here`, `Ask assistant`, `Review proposals`, `Add card`)
  - board-aware deep links from inbox/notifications/review
- `#96` and `#100` should preserve:
  - dismissible in-app help blocks and help-center shape
  - manual structure aligned to top-level product navigation
- `#328` should preserve:
  - the `novice-first-first-run` scenario shape
  - launch criteria and first-run smoke synchronization

### Secondary backlog now seeded as a lower-priority follow-through wave

- tracker:
  - `#329` secondary MVP follow-through tracker
- product evidence / demoability follow-through:
  - `#330` in-app `Demo Tools`, guided narrative/demo-tour, nav badges, hero-board quality
- harness maturity follow-through:
  - `#331` static HTML demo report, snapshot/trace assertions, director presets, soak mode
  - `#332` replay-from-trace and scenario-authoring follow-through
- productivity/import follow-through:
  - `#333` saved views and post-Wave-P productivity shortcuts
  - `#334` broader note-style import and clip intake follow-through

These issues are intentionally lower priority than Wave P and should not compete with the `#318` to `#328` tranche.

### Seeded later-wave architecture from the expanded blueprint

- tracker:
  - `#335` expanded blueprint architecture tracker
- agent substrate:
  - `#336` agent profile/run/event foundation and manual-run API
  - `#337` tool registry, policy evaluator, and first bounded template
  - `#338` agent mode surfaces and run-detail timeline
- knowledge and integrations:
  - `#339` knowledge document and SQLite FTS foundation
  - `#340` integrations registry and supervised inbound connector foundation
- telemetry and release framing:
  - `#341` product telemetry taxonomy and `R1` / `R2` / `R3` launch-gate follow-through

## Audit Decision

- Keep `MINIMAL` as the near-horizon filter.
- Keep `EXPANDED` as the staged roadmap and architecture source.
- Keep the source files in `docs/InReview/MVP_EXPANSION/` non-authoritative.
- Do not let any high-signal row from this audit disappear into `partial` without either:
  - a canonical doc reference,
  - a numbered issue,
  - or an explicit deferred note.
