# 2026-03-07 MVP Expansion Source Coverage Audit

Status: Non-authoritative audit record.

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
- The secondary backlog from the full pack is still unevenly represented:
  - some items are intentionally deferred
  - some are partially mapped to reuse anchors
  - some remain unseeded

## Source-to-Target Matrix

### MINIMAL Pack

| Source | Key signal | Current target(s) | Status | Carry-forward note |
|---|---|---|---|---|
| `docs/InReview/MVP_EXPANSION/MINIMAL/taskdeck_review_2026-03-06/00_READ_ME_FIRST.md` | demo infrastructure is ahead of self-serve product clarity | `README.md`, `docs/STATUS.md`, `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/analysis/2026-03-07_mvp-expansion-reconciliation-tracker.md`, `#318` to `#328` | promoted + seeded | in-app demo/self-serve affordances are still deferred |
| `docs/InReview/MVP_EXPANSION/MINIMAL/taskdeck_review_2026-03-06/01_DEMO_COMPLETENESS_ASSESSMENT.md` | distinguish demo-as-proof from self-serve product onboarding | `docs/STATUS.md`, `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/DEMO_PLAYBOOK.md`, `docs/TESTING_GUIDE.md` | partial | `Demo Tools`, guided narrative/demo-tour, and hero-board quality are not explicitly issue-tracked yet |
| `docs/InReview/MVP_EXPANSION/MINIMAL/taskdeck_review_2026-03-06/02_GOLDEN_PATH_AND_PRODUCTIZATION.md` | surface taxonomy, start surface, board context travel, selectors, readable proposals, empty/help states | `docs/START_HERE.md`, `docs/USER_MANUAL.md`, `docs/STATUS.md`, `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/ISSUE_EXECUTION_GUIDE.md`, `#320`, `#322`, `#324`, `#326` | promoted + seeded | board-aware quick capture and exact CTA patterns should stay explicit in `#326` implementation |
| `docs/InReview/MVP_EXPANSION/MINIMAL/taskdeck_review_2026-03-06/03_DOGFOODING_AND_USEFUL_NOW.md` | honest useful-now posture, solo-dev dogfooding shape, friction metrics | `docs/DOGFOODING_GUIDE.md`, `docs/USER_MANUAL.md`, `README.md` | partial | exact board/column/label defaults, metric taxonomy, and saved-views follow-through remain weakly represented |
| `docs/InReview/MVP_EXPANSION/MINIMAL/taskdeck_review_2026-03-06/04_SCENARIO_MATRIX_AND_TEST_PLAN.md` | tiered product smoke/scenario/live/adversarial testing strategy | `docs/TESTING_GUIDE.md`, `docs/SCENARIOS.md`, `#328` | partial | named scenario matrix, snapshot/trace assertions, HTML report, presets, soak mode, and internal scenario composer are not yet preserved as explicit backlog items |
| `docs/InReview/MVP_EXPANSION/MINIMAL/taskdeck_review_2026-03-06/05_MANUAL_AND_DOCS_STRATEGY.md` | audience-layered docs set with `START_HERE` as bridge doc | `README.md`, `docs/START_HERE.md`, `docs/USER_MANUAL.md`, `docs/INDEX.md`, `#100` | promoted + seeded | screenshot/gif placement and fuller manual appendix structure remain later maturity work |
| `docs/InReview/MVP_EXPANSION/MINIMAL/taskdeck_review_2026-03-06/06_PRIORITIZED_BACKLOG.md` | Wave I ordering plus secondary demoability/harness/productivity/agent backlog | `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/ISSUE_EXECUTION_GUIDE.md`, `docs/TaskdeckNextWorkChecklist.md`, `#318` to `#328` | core promoted; secondary deferred | `Demo Tools`, guided narrative, nav badges, hero-board quality, HTML report, saved views, replay-from-trace, and scenario composer are still not sharply mapped |
| `docs/InReview/MVP_EXPANSION/MINIMAL/taskdeck_review_2026-03-06/INDEX.md` | pack inventory only | `docs/analysis/2026-03-07_mvp-expansion-reconciliation-tracker.md`, this audit | accounted for | no extra action needed |
| `docs/InReview/MVP_EXPANSION/MINIMAL/taskdeck_review_2026-03-06/TASKDECK_REVIEW_MASTER.md` | compiled copy of the numbered review files | same union as rows above | accounted for | treat as convenience artifact, not a second canonical source |

### EXPANDED Pack

| Source | Key signal | Current target(s) | Status | Carry-forward note |
|---|---|---|---|---|
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/00_MASTER_BLUEPRINT.md` | one core system, three modes, sequence human product before autonomy breadth | `docs/STATUS.md`, `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/ISSUE_EXECUTION_GUIDE.md`, `#318` to `#328` | promoted | exact mode contract should stay explicit during Wave I implementation |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/01_PRODUCT_STRUCTURE_AND_POSITIONING.md` | concrete nav architecture, route taxonomy, novice vocabulary | `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/USER_MANUAL.md`, `#320`, `#322` | partial | exact route map and vocabulary table are not yet preserved in canonical docs |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/02_GOLDEN_PATHS_AND_UX_SHAPE.md` | page specs for `Home`, `Review`, `Today`, action states, no raw IDs | `docs/START_HERE.md`, `docs/TESTING_GUIDE.md`, `docs/IMPLEMENTATION_MASTERPLAN.md`, `#320`, `#322`, `#324`, `#326`, `#328` | promoted + seeded | plain-language top-box and page-spec details are only partially preserved |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/03_FRONTEND_PRODUCTIZATION_PLAN.md` | concrete frontend execution order and file-level plan | `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/TaskdeckNextWorkChecklist.md`, `#320`, `#322`, `#324`, `#326`, `#96`, `#93`, `#328` | partial | file-order execution, minimum polish bar, and explicit board-aware UI patterns should remain visible during implementation |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/04_BACKEND_AND_DOMAIN_EXPANSION_PLAN.md` | aggregate workspace APIs, user preferences, proposal summary service, agent/tool/policy direction | `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/ISSUE_EXECUTION_GUIDE.md`, `#320`, `#324`, `#326` | partial | `UserPreference`, aggregate API rule, `IProposalSummaryService`, and tool/policy abstractions remain shallowly represented |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/05_AGENT_WORKSPACE_ARCHITECTURE.md` | `AgentProfile`, `AgentRun`, `AgentRunEvent`, tool registry, policy evaluator, run views | `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/STATUS.md`, `docs/TaskdeckNextWorkChecklist.md` | partial + unseeded | no canonical numbered issue set exists yet for the concrete agent substrate |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/06_INTEGRATIONS_KNOWLEDGE_AND_AUTONOMY.md` | knowledge model, SQLite FTS, clipper/import connectors, integrations page, board assistant panels | `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/TaskdeckNextWorkChecklist.md`, `#75`, `#98`, `#218`, `#219` | partial + unseeded | `KnowledgeDocument`, FTS search, integrations registry, clipper, and assistant-panel work remain largely unseeded |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/07_TESTING_METRICS_AND_OPERATIONS.md` | first-run smoke, telemetry/event taxonomy, launch gates, product-quality stack | `docs/TESTING_GUIDE.md`, `docs/IMPLEMENTATION_MASTERPLAN.md`, `#328`, `#77` | partial | detailed novice-beta/agent-alpha telemetry and release gating are not yet captured beyond broad launch-criteria notes |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/08_SEEDED_ISSUES_READY_TO_CREATE.md` | issue-ready decomposition of Epics A-E | `#318`, `#320`, `#322`, `#324`, `#326`, `#96`, `#100`, `#328`, `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/ISSUE_EXECUTION_GUIDE.md` | EPIC A/B/E1-E3 partially seeded | EPIC C, EPIC D, and the E4 telemetry/dashboard slice are still not fully seeded |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/09_COMPREHENSIVE_MANUAL_BLUEPRINT.md` | manual structure aligned to top-level product navigation and in-app help mapping | `docs/USER_MANUAL.md`, `docs/START_HERE.md`, `docs/INDEX.md`, `#100` | partial | manual/help-center structure still reflects current workbench reality more than future shell/navigation |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/10_PHASED_ROADMAP_AND_RELEASE_PLAN.md` | phased sequence and `R1` / `R2` / `R3` release framing | `docs/IMPLEMENTATION_MASTERPLAN.md` | partial | the phase order is carried; the release shorthand and anti-roadmap rules are not yet preserved explicitly |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/11_RISKS_NON_GOALS_AND_DECISION_RULES.md` | scope-control rules and anti-pattern guardrails | `docs/STATUS.md`, `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/ISSUE_EXECUTION_GUIDE.md` | promoted | the exact decision-rule wording is not yet codified as a reusable implementation checklist |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/INDEX.md` | pack read order and summary | `docs/analysis/2026-03-07_mvp-expansion-reconciliation-tracker.md`, this audit | accounted for | no extra action needed |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/TASKDECK_EXPANSION_MASTER.md` | compiled copy of the numbered blueprint files | same union as rows above | accounted for | treat as convenience artifact, not a second canonical source |

### Snippet Assets

| Source | Key signal | Current target(s) | Status | Carry-forward note |
|---|---|---|---|---|
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/snippets/backend/AgentProfile.cs` | board/workspace-scoped agent identity with template key and policy JSON | `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/TaskdeckNextWorkChecklist.md` | unseeded | concrete agent entity/API issue set still missing |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/snippets/backend/AgentRun.cs` | inspectable run entity with explicit statuses and proposal linkage | `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/TaskdeckNextWorkChecklist.md` | unseeded | run lifecycle is described only at horizon level |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/snippets/backend/AgentRunService.cs` | agent runtime should gather context, evaluate policy, invoke tool registry, and create proposals | `docs/IMPLEMENTATION_MASTERPLAN.md` | unseeded | `IAgentPolicyEvaluator` and `ITaskdeckToolRegistry` need future issue coverage |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/snippets/backend/AgentsController.cs` | first agent CRUD/manual-run API slice | `docs/IMPLEMENTATION_MASTERPLAN.md` | unseeded | concrete controller/API slice not yet issue-tracked |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/snippets/backend/HomeController.cs` | product-shaped aggregate endpoints belong under `/api/workspace/*` | `#320`, `#324`, `docs/IMPLEMENTATION_MASTERPLAN.md` | partial + seeded | aggregate-API rule should stay explicit during Wave I |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/snippets/frontend/HomeView.vue` | `Home` needs first-run state, urgent-work counts, recent boards, and clear CTAs | `#320`, `docs/IMPLEMENTATION_MASTERPLAN.md` | partial + seeded | CTA set and first-run state should not collapse into a generic dashboard |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/snippets/frontend/ProposalSummaryCard.vue` | proposal cards need plain-language summary, risk/source chips, affected entities, and direct actions | `#326`, `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/TESTING_GUIDE.md` | partial + seeded | backend summary service and entity/deep-link behavior should stay explicit |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/snippets/frontend/workspaceModeStore.ts` | workspace mode is first-class product state, not a temporary feature-flag proxy | `#320`, `docs/IMPLEMENTATION_MASTERPLAN.md` | partial + seeded | durable server-backed preference should remain in scope when practical |
| `docs/InReview/MVP_EXPANSION/EXPANDED/taskdeck_expansion_blueprint_2026-03-06/snippets/scenarios/novice-first-first-run.json` | concrete first-run acceptance scenario for `Home -> capture -> review -> board` | `docs/SCENARIOS.md`, `#328`, `docs/TESTING_GUIDE.md` | partial + seeded | scenario should guide the final first-run smoke instead of remaining only a note |

## Explicit Carry-Forward After Audit

### Wave I implementation details that should stay explicit

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

### Secondary backlog preserved but not yet seeded cleanly

- product evidence / demoability follow-through:
  - in-app `Demo Tools`
  - guided narrative/demo tour
  - nav badges
  - hero-board quality
- harness maturity follow-through:
  - static HTML demo report
  - snapshot assertions
  - trace assertions
  - director narrative presets
  - long-run soak mode
  - replay-from-trace
  - internal scenario composer/editor
- productivity follow-through:
  - saved views
  - broader import surfaces beyond current anchors

### Still-unseeded architecture work

- agent workspace foundation:
  - `AgentProfile`
  - `AgentRun`
  - `AgentRunEvent`
  - tool registry
  - policy evaluator
  - first narrow template
  - agent-mode views
- knowledge and integrations foundation:
  - `KnowledgeDocument`
  - `KnowledgeChunk`
  - SQLite FTS search
  - note/transcript/clip intake
  - integrations registry/management surface

## Audit Decision

- Keep `MINIMAL` as the near-horizon filter.
- Keep `EXPANDED` as the staged roadmap and architecture source.
- Keep the source files in `docs/InReview/MVP_EXPANSION/` non-authoritative.
- Do not let any high-signal row from this audit disappear into `partial` without either:
  - a canonical doc reference,
  - a numbered issue,
  - or an explicit deferred note.
