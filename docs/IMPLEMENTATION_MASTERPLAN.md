# Taskdeck Implementation Masterplan

Last Updated: 2026-04-09
<br>
Planning Horizon: Next 8 to 12 weeks  
Companion Active Docs:
- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/TESTING_GUIDE.md`
- `docs/MANUAL_TEST_CHECKLIST.md`
- `docs/GOLDEN_PRINCIPLES.md`

## Purpose

This is the active execution guide for sequencing past, current, and future implementation.
`docs/STATUS.md` is authoritative for current shipped reality; this document tracks delivery history, planned work, roadmap sequencing, and strategic intentions.
Update this file at the end of each meaningful delivery cycle or when new work is seeded.

## Planning Principles

- `docs/STATUS.md` is authoritative for current shipped reality.
- Product north star: make capture nearly free and keep automation safe through review-first proposals.
- Product legibility is now the immediate product focus: the app should explain its core loop from inside the UI, not mainly through docs and demo scripts.
- For near-horizon demo work, prefer packaging the shipped capture/review/board substrate into stakeholder-legible business workflows instead of reopening broad architecture.
- Prefer finishing cross-cutting consistency work before adding new surface area.
- Security and identity convergence remains the highest-priority engineering track.
- Cross-user existence policy is fixed: return `403` for authenticated-but-unauthorized access and `404` for true missing resources.
- Automation remains proposal-first and review-first by default.
- Do not claim or ship silent/destructive autonomy by default; trust posture takes precedence over convenience.
- MVP should include a dogfooding workflow: paste structured plan text in chat and bootstrap a board/project from approved proposals.
- UX investments should be modular and reusable (keyboard-first, discoverable selectors, shared input-assist patterns).
- Use `docs/InReview/MVP_EXPANSION/MINIMAL/` as the near-horizon execution filter and `docs/InReview/MVP_EXPANSION/EXPANDED/` as the staged roadmap reference.
- Do not add major new surface breadth ahead of `Home` / `Today` / `Review` productization unless the work closes a real trust, safety, or operability gap.
- Agent, knowledge, and integrations expansion stay sequenced behind novice-first productization even though their longer-term architecture is now clearer.
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
   - Backend: 1975+ passing (property-based and fuzz tests added via FsCheck)
   - Frontend unit: 1491+ passing (134+ test files; batch triage, search, accessibility tests added)
   - Default Playwright regression lane: 24+ passing (accessibility axe-core E2E added; `stakeholder-demo.spec.ts` remains opt-in/skipped by default)
8. Documentation consolidation retained:
   - active docs remain focused at `docs/` root
   - detail packs/audits archived under `docs/archive/2026-02-13_phase4-doc-consolidation/`
9. Wave P docs/help follow-through delivered:
   - `docs/START_HERE.md` now matches the shipped `Home` / `Today` / `Inbox` / `Review` / `Boards` shell
   - `docs/USER_MANUAL.md` now acts as the shipped-product manual index for the novice-first shell
   - `docs/manual/README.md`, `docs/manual/*`, and the new product help guides now carry the chaptered workflow, FAQ, troubleshooting, and help-center follow-through without pretending later `Agents` / `Integrations` breadth is already shipped
   - `docs/INDEX.md` and `docs/product/README.md` now make the root-doc, manual-chapter, and product-help split explicit
10. Stage 0 governance follow-through:
   - active docs cross-link/date-stamp freeze completed for canonical docs
   - project safety view standardized as `No Status` (`no:status`)
   - weekly backlog seeding cadence and RC hard-gate policy documented in active ops docs
11. Security convergence progress:
   - `[Authorize]` enforced across remaining legacy controller families
   - claims-first identity retrofit delivered for columns/cards/labels/export/queue/board-access
   - caller-supplied actor query/body IDs removed from those controller families
   - API integration suite expanded for legacy unauthorized/forbidden/cross-user regression checks
   - API integration suite expanded for legacy unauthorized-path regression checks
12. Frontend runtime alignment:
   - added a lightweight static UI mock at `frontend/taskdeck-web/public/mock/` so the current product shell and key surfaces can be previewed from local example data without backend/runtime setup
   - added a dedicated GitHub Pages Actions workflow that publishes `frontend/taskdeck-web/public/mock/` directly as the Pages site root, replacing the earlier branch-based `main` + `/docs` publish path
   - CI and local developer baseline pinned to Node 24.13.1 (LTS) to match Vite 7 engine constraints
13. Security convergence completion for remaining legacy families:
   - audit controller now derives actor identity from claims for user-history and board-history access checks
   - users controller now enforces self-scope for read/update/activate/deactivate profile actions
   - audit frontend flow moved from user-id route calls to `/audit/users/me`
14. SEC-03 regression matrix delivery:
   - added explicit API integration matrix assertions for protected legacy + advanced routes
   - expanded policy coverage for `401` unauthenticated, `403` cross-user unauthorized, and `404` true missing resources
15. SEC-04 API error-contract assertions delivery:
   - middleware-level JWT challenge/forbidden responses now emit stable `ApiErrorResponse` payloads
   - API integration assertions now explicitly enforce auth and validation error-contract shape stability
16. UX-01 archive lifecycle coherence delivery:
   - board settings archive action now reflects soft-delete semantics (reversible archive, not permanent deletion)
   - archive workspace now surfaces archived boards and supports restore via board lifecycle API flow
   - API integration roundtrip coverage added for archive-to-restore board lifecycle behavior
17. UX-02 drag/edit interaction safety guardrails delivery:
   - card and column drag now requires explicit drag handles
   - non-handle drag gestures are ignored to prevent accidental movement during adjacent edit interactions
   - frontend unit + E2E coverage added for handle-only drag behavior and conflict paths
18. UX-03 command palette keyboard model delivery:
   - command palette now supports keyboard-first filtering, item selection, and activation
   - shell interactions preserve deterministic close behavior (`Escape`) and focus handling
   - frontend unit + E2E coverage added for command palette keyboard navigation and activation
19. UX-04 activity selector discoverability delivery:
   - activity workflows now prioritize selector-first board/entity/user discovery instead of raw ID-first entry
   - board/entity selection now includes discoverable context and ID reveal/copy affordance
   - frontend unit + E2E coverage added for selector-based activity navigation and fetch flows
20. UX-04 shared input-assist scaffolding delivery:
   - shared input-assist combobox/listbox component added for reusable suggestion and keyboard-selection behavior
   - ops CLI template selection now uses input-assist with discoverable template metadata
   - automation chat board targeting now uses input-assist board suggestions with keyboard-first interactions
21. UX-05 escape behavior contract delivery:
   - workspace and board escape handling now follows a top-surface-first contract via shared escape-stack handling
   - board routes now exit to `/workspace/boards` when no transient surface is open
   - unit + E2E regression coverage validates escape ordering and board-exit behavior
22. AUTO-01 real-provider strategy delivery:
   - `ILlmProvider` selection now follows deterministic environment-aware policy evaluation (`Mock` vs `OpenAI`)
   - live provider usage is explicitly gated by config (`EnableLiveProviders`, provider mode, development override guard)
   - OpenAI provider path and policy constraints are test-backed while preserving proposal-first chat flow semantics
23. AUTO-02 planner/executor hardening delivery:
   - planner instruction coverage now includes deterministic board/column intents (rename/archive/unarchive/reorder) with explicit board/position validation
   - executor operation parameter parsing now fails with deterministic validation errors instead of exception-driven fallbacks
   - partial-failure behavior is test-backed as transactional rollback + proposal failure status update with actionable operation-sequenced reasoning and improved audit entity attribution
24. MVP-01 chat-to-project bootstrap delivery:
   - chat now supports canonical Markdown checklist ingestion and proposal-first bootstrap operation generation for board-scoped sessions
   - proposal review remains mandatory, with chat exposing one-click approve + execute action for generated checklist bootstrap proposals
   - backend + API + frontend tests cover canonical happy path and key checklist parse/validation failures
25. PACK-01 starter-pack manifest foundation delivery:
   - added a versioned starter-pack manifest contract (`schemaVersion` `1.0`) for labels, columns, templates, and seed cards
   - added deterministic backend parsing/validation service with explicit compatibility and cross-reference constraints
   - added dedicated application tests covering canonical success + key parse/validation failure paths
26. PACK-01 null-collection hardening follow-up:
   - manifest validation now handles explicit JSON `null` collections deterministically (array-shape errors instead of null-reference exceptions)
   - nested collection paths (`compatibility.requiredFeatures`, template checklists, seed-card labels) are now null-safe and regression-tested
27. PACK-02 starter-pack apply backend delivery:
   - added authenticated board-scoped apply endpoint: `POST /api/boards/{boardId}/starter-packs/apply`
   - delivered idempotent apply semantics with dry-run actionable conflict reporting for labels/columns/seed-card references
   - added API integration coverage for apply success, re-apply idempotency, dry-run conflict report, and non-dry-run conflict response
28. PACK-03 starter-pack frontend catalog delivery:
   - added board-level starter pack catalog UI with search/filter and manifest preview details
   - integrated dry-run preview and one-click apply flow against the backend apply endpoint
   - added frontend API + component interaction tests for preview/apply/conflict/empty states
29. PACK-04 first-party starter packs v1 delivery:
   - added API-backed first-party starter-pack catalog endpoint: `GET /api/boards/{boardId}/starter-packs/catalog`
   - shipped first-party pack coverage for common labels, common column flow, and 3 board blueprints
   - added backend/frontend tests for catalog availability, pack-category coverage, and manifest validity
30. PACK-05 deterministic fixture packs delivery:
   - added Playwright starter-pack fixture bootstrap helper flow for manifest-backed deterministic board-state setup
   - shipped deterministic fixture manifests for `small`, `medium`, and `edge` scenarios
   - added dedicated E2E coverage for fixture bootstrap success and conflict dry-run paths
31. DEBT-01 nullability reduction delivery:
   - eliminated current domain `CS8618` warnings by applying EF-safe non-null default initialization patterns
   - validated no behavior regressions via full backend solution test pass
32. DEBT-02 log-query scalability pass delivery:
   - replaced broad in-memory log composition with repository-filtered query paths
   - removed command-run log query N+1 pattern by introducing direct filtered log querying with run correlation/user projection
   - validated logs API contract behavior and full backend regression suite pass
33. DEBT-03 database export/import delivery:
   - added authenticated database export/import API routes (`GET /api/export/database`, `POST /api/import/database`)
   - implemented minimal-safe SQLite file export/import with Development-sandbox gating, payload signature/size validation, and backup-restore fallback on file replacement failure
   - added application and API integration coverage for auth, sandbox gating, and import validation paths
34. COL-01 realtime board updates delivery:
   - added SignalR `BoardsHub` with claims-derived board subscription authz checks and board-scoped group subscriptions
   - added application-layer board mutation notifications for board/card/column/label writes and wired hub fan-out notifier in API composition root
   - integrated frontend board realtime lifecycle (join/switch/leave/reconnect) with websocket-unavailable polling fallback and expanded API/unit/E2E regression coverage
35. OBS-01 observability baseline delivery:
   - added OpenTelemetry startup wiring for ASP.NET + HttpClient instrumentation with Taskdeck custom activity source and meter registration
   - added worker/queue/heartbeat telemetry emission with stable metric names and dimension keys
   - added correlation ID propagation into trace tags plus a versioned observability baseline runbook with dashboard/alert/smoke-verification guidance
36. OPS-07 containerized deployment baseline delivery:
   - added production-oriented backend/frontend Dockerfiles and compose profile with reverse-proxy entrypoint
   - added proxy compression + forwarded-header/security-header posture and staging/local deployment runbook
   - added CI container image build/export artifacts with reproducible compose render checksums
37. Developer MCP tooling posture expansion:
   - enabled a broader Docker Marketplace MCP server bundle (SQLite, JetBrains, Postman candidate, OpenAPI, filesystem, terraform, time, etc.)
   - stabilized default Docker gateway server set for Codex project config to avoid secret-gated startup failures while preserving optional integrations
   - documented setup/credential expectations in `docs/MCP_TOOLING_GUIDE.md`
38. MCP operations workflow integration:
   - added operator runbook (`docs/tooling/MCP_OPERATIONS_RUNBOOK.md`) covering credential setup, validation, troubleshooting, and recurring checklists
   - added helper scripts to wire credential-gated Docker MCP servers and verify baseline/optional MCP dry-run paths
   - integrated MCP operations checks into active testing guidance
39. TST-07 MCP smoke/regression harness delivery:
   - enhanced MCP profile validation script with optional-server prerequisite diagnostics (missing secret/config classification)
   - codified strict/warning/skip behavior for optional integrations and documented CI-friendly command patterns
   - added deterministic CI status output contract (`PASS`, `PASS_WITH_WARNINGS`, `FAIL`) for MCP profile validation flows
40. OPS-19 CI topology first-pass delivery:
   - migrated required CI entrypoint from `.github/workflows/ci.yml` to `.github/workflows/ci-required.yml` with equivalent gate behavior
   - extracted docs governance lane into reusable workflow `.github/workflows/reusable-docs-governance.yml` as baseline for incremental workflow decomposition
41. OPS-19 CI topology second-pass delivery:
   - extracted backend architecture lane into reusable workflow `.github/workflows/reusable-backend-architecture.yml` and routed `ci-required.yml` through it
   - extracted frontend unit lane into reusable workflow `.github/workflows/reusable-frontend-unit.yml` (preserving Ubuntu/Windows matrix behavior) and routed `ci-required.yml` through it
42. OPS-19 CI topology API-integration extraction delivery:
   - extracted API integration lane into reusable workflow `.github/workflows/reusable-api-integration.yml` and routed `ci-required.yml` through it (preserving Ubuntu/Windows matrix behavior)
43. OPS-19 CI topology third-pass delivery:
   - added `merge_group` trigger parity to `.github/workflows/ci-required.yml` to align merge-queue required-check execution with PR/push paths
44. OPS-19 CI topology fourth-pass delivery:
   - extracted backend unit lane into reusable workflow `.github/workflows/reusable-backend-unit.yml` (preserving Ubuntu/Windows matrix behavior and domain/application/CLI split coverage)
   - routed `.github/workflows/ci-required.yml` through the reusable backend unit lane
45. OPS-19 CI topology fifth-pass delivery:
   - extracted container image lane into reusable workflow `.github/workflows/reusable-container-images.yml` and routed `ci-required.yml` through it
   - extracted E2E smoke lane into reusable workflow `.github/workflows/reusable-e2e-smoke.yml` and routed `ci-required.yml` through it while preserving required-gate dependency ordering and artifact upload behavior
45. SEC-11 cross-user convergence progress (`#152`):
   - automation proposal lifecycle endpoints now enforce proposal-scope authorization (`get/approve/reject/execute/diff`) via board read/write permission or requester-only fallback for user-scoped proposals
   - API integration authz matrix expanded for additional protected automation/logs/starter-pack routes with `401` assertions, plus focused `403` and `404` regression tests for proposal, logs correlation, and starter-pack apply paths
46. AUTH-06 register/login hardening progress (`#174`):
   - login flow now avoids inactive-candidate short-circuit lockout in identifier-collision paths by preferring active password matches before returning inactive-account errors
   - duplicate registration now returns actionable conflict guidance to steer users toward existing-account sign-in
   - regression coverage added for duplicate-register-then-login success sequence and explicit invalid-credentials (`401`) vs inactive-account (`403`) API contract behavior, with frontend session-flow regression for non-poisoned post-error login
47. SEC-11 archive authorization follow-through (`#152`):
   - archive list/detail/entity-lookup read paths now require caller board-read permission and return deterministic `Forbidden` payloads for cross-user unauthorized access
   - board-filtered archive queries now fail fast with `403` when caller cannot read the target board, while preserving `404` for true missing archive resources
   - regression coverage expanded in application and API integration suites for archive authorization enforcement and board cross-user policy behavior
48. SEC-11 audit entity-history authorization follow-through (`#152`):
   - `GET /api/audit/entities/{entityType}/{entityId}` now resolves board-scoped entities (`Board`, `Column`, `Card`, `Label`) before querying history and enforces caller board-read permissions
   - endpoint semantics now align to policy for entity history requests (`403` for authenticated cross-user unauthorized access, `404` for true missing board-scoped entities)
   - API integration coverage expanded in `AuditApiTests` and `AuthzRegressionMatrixApiTests` to lock unauthorized/cross-user/missing-resource behavior
49. OPS-19 CI topology sixth-pass progress (`#168`):
   - added non-blocking CI orchestrator (`.github/workflows/ci-extended.yml`) with actionlint + dependency-review lanes and opt-in (`testing` label/manual) backend/E2E regression jobs
   - added scheduled/manual nightly orchestrator (`.github/workflows/ci-nightly.yml`) for backend solution regression, E2E regression, and container-image regression
   - added release/security orchestrator (`.github/workflows/release-security.yml`) with dependency inventory/vulnerability reporting artifacts and explicit SBOM/provenance follow-through mapping to `#103`
   - added reusable full backend regression lane (`.github/workflows/reusable-backend-solution.yml`) to avoid orchestration-layer command duplication
50. OPS-19 CI topology completion (`#168`):
   - added `ci-release.yml` release build-verification lane with SBOM/provenance placeholder, container image artifact lane
   - added comprehensive workflow topology documentation to `ci-required.yml` header comment mapping all orchestrators and reusable workflows
   - added topology reference comments to `ci-extended.yml`, `ci-nightly.yml`, `nightly-quality.yml`, and `release-security.yml`
   - added `CODEOWNERS` file for `.github/workflows/`, issue templates, PR template, and governance scripts
   - updated CI Status section in `docs/STATUS.md` to reflect the complete topology including `ci-release.yml` and `nightly-quality.yml`
51. SEC-11 LLM queue board-scope authorization follow-through (`#152`):
   - `POST /api/llm-queue` now enforces board-read authorization when `boardId` is supplied
   - queue creation now aligns to policy (`403` for authenticated cross-user unauthorized board access, `404` for true missing boards)
   - regression coverage expanded in `LlmQueueServiceTests`, `LlmQueueApiTests`, and `AuthzRegressionMatrixApiTests`
52. SEC-11 API regression coverage final sweep (`#152`):
   - expanded cross-user `403` coverage for board update and board-access management (`list/grant/update/revoke`)
   - expanded chat authorization coverage for cross-user forbidden access and true-missing session `404` branches (`get session`, `send message`)
   - API integration suite increased to 185 passing tests with explicit `403/404` branch locking for remaining protected route gaps
53. API-06 centralized exception/fallback error-contract hardening (`#153`):
   - added global unhandled-exception middleware in the API pipeline to return deterministic `ApiErrorResponse` payloads for unexpected server failures
   - standardized unknown-result fallback `500` mapping to `ApiErrorResponse` (`UnexpectedError`) instead of `ProblemDetails` to keep fallback payload shape contract-uniform
   - added fault-injection API integration coverage validating unhandled-failure contract shape, non-leakage message behavior, and correlation-header continuity under `500` responses
54. TST-14 architecture-guard expansion (`#157`):
   - expanded architecture tests beyond csproj references with source-layer purity invariants for Domain/Application forbidden namespace imports
   - added API controller boundary invariants to restrict direct `ControllerBase` inheritance to auth/health controllers and enforce `[Authorize]` declaration on protected controllers
   - architecture guard suite now emits deterministic file-scoped diagnostics for quick remediation in CI and local runs
55. TST-01 load/concurrency harness delivery (`#70`):
   - added k6 board-heavy API regression profile (`tests/load/k6/board-heavy-load.js`) with seeded-auth setup, read/write traffic mix, thresholds, and failure diagnostics
   - added multi-session Playwright concurrency harness coverage (`frontend/taskdeck-web/tests/e2e/concurrency.spec.ts`) for conflicting edits and realtime cross-session propagation
   - added reusable CI lane (`.github/workflows/reusable-load-concurrency-harness.yml`) and wired it into `ci-extended` (testing label/manual) plus `ci-nightly` with persisted k6/Playwright artifacts
56. ARCH-01 multi-tenancy strategy ADR delivery (`#71`):
   - added accepted ADR at `docs/analysis/2026-02-22_multi-tenancy-strategy-adr.md` comparing `database-per-tenant`, `schema-per-tenant`, and `shared-schema + TenantId`
   - selected `shared-schema + TenantId` as immediate rollout model with explicit promotion path to `database-per-tenant` for high-isolation tiers
   - defined phased migration/enforcement plan plus tenant-isolation readiness checklist and cross-tenant `403` test strategy expectations
57. FE-11 frontend lint baseline + CI enforcement (`#154`):
   - added pragmatic Vue 3 + TypeScript ESLint baseline (`.eslintrc.cjs`) with focused rule suppressions to avoid style-churn while catching correctness issues
   - added `npm run lint` script with zero-warning enforcement and integrated lint into reusable frontend CI lane (`reusable-frontend-unit.yml`)
   - documented frontend lint execution and suppression guidance in active testing docs to keep lint policy explicit for contributors
58. FE-12 frontend coverage threshold gate (`#155`):
   - codified global and critical-surface Vitest coverage thresholds (`src/api`, `src/store`, `src/composables`, `src/utils`, `src/components/board`) in frontend test configuration
   - switched reusable frontend CI lane to threshold-enforced coverage execution and standardized machine-readable triage artifacts (JUnit + coverage JSON/HTML)
   - documented explicit ratchet policy (thresholds can remain or increase, never decrease) and local threshold-breach verification command
59. COL-02 notifications framework delivery (`#72`):
   - added notification persistence model (`Notifications`, `NotificationPreferences`) with user-scoped preference toggles for event-family cadence controls and in-app channel enablement
   - shipped authenticated notification APIs (`GET /api/notifications`, `POST /api/notifications/{id}/read`, `GET/PUT /api/notifications/preferences`) with board-filter authorization guardrails and deduplication-aware publish semantics
   - integrated frontend notification inbox/preferences routes + Pinia store/api clients and added regression coverage for backend event publication, API auth/filter behavior, and frontend inbox/preferences interactions
60. COL-03 collaborative presence/conflict policy delivery (`#73`):
   - added SignalR-backed board/card presence snapshots with active viewer/editor state publication on join/leave/disconnect and card editing focus changes
   - added optimistic card update conflict policy via `ExpectedUpdatedAt` with deterministic `409 Conflict` user feedback and stale-write conflict audit logging (actor + expected/actual timestamps)
   - expanded backend/frontend regression coverage, including multi-session Playwright conflict scenario validation and realtime presence broadcast assertions
61. COL-04 threaded card comments and mentions workflow delivery (`#74`):
   - added authenticated board/card comment APIs for create/list/reply/update/delete with reply-depth guardrails and moderation constraints (author or board owner/admin)
   - added mention parsing + actor-linking for card comment bodies with board-read permission checks before mention notification publication
   - added card-comment audit entries and frontend card-modal comment UI flow (thread list, reply, edit, delete), with backend/frontend test coverage for mention parsing and authorization boundaries
62. Capture realignment backlog seeding delivery (`#199` to `#213`):
   - reconciled in-review capture/security/performance planning packs into dependency-mapped GitHub issues
   - seeded a dedicated capture wave tracker (`#199`) with execution issues (`#200` to `#211`) plus linked security/performance follow-through (`#212`, `#213`)
63. UX-15 review-first routing and selector cleanup delivery (`#322`):
   - `/workspace/review` is now the canonical normal-user automation route, with legacy proposals URLs redirected compatibly and shell/home/inbox/card links pointed at Review
   - queue, chat, ops, and access surfaces now explain their advanced/operator purpose in plain language and expose action-oriented next steps instead of orphan empty states
   - board access now uses a board picker, automation chat accepts selector-safe board context instead of raw-ID happy paths, and frontend unit + Playwright coverage now locks selector flow, route defaults, and representative empty-state branches
   - linked follow-through status is now split: `#212` delivered the logging/telemetry redaction policy and runtime guardrails; `#213` delivered frontend list virtualization (inbox + activity views) using `@tanstack/vue-virtual`
   - updated existing SEC-06 rate-limiting issue (`#81`) and wave index (`#107`) to integrate capture-specific scope without duplicate issue creation
64. InReview extraction coverage expansion (`#216` to `#220`):
   - seeded go-to-market and user-research execution issues from HUMAN playbooks (`#216`, `#217`)
   - seeded deferred capture follow-ons from the original realignment pack (`#218`, `#219`, `#220`)
   - updated capture wave tracker (`#199`) and wave index (`#107`) to keep extraction coverage explicit
65. CAP-01 capture model/domain contract delivery (`#200`):
   - accepted queue-wrapper MVP model (`LlmRequest` + `inbox.capture.v1`) with explicit migration path to dedicated capture entities
   - added canonical capture source/status contracts plus transition policy mapping from queue lifecycle states
   - added capture payload schema/invariant enforcement (schema version, raw text bounds, actor-field rejection) and provenance linkage representation for capture item -> triage run -> proposal
66. CAP-03 queue provenance fix delivery (`#202`):
   - extended planner contract to support explicit source metadata (`sourceType`, `sourceReferenceId`, `correlationId`) with manual-safe defaults
   - queue worker now stamps queue-origin proposals as `ProposalSourceType.Queue` instead of `Manual`
   - queue item id is now forwarded as source-reference and correlation metadata for deterministic provenance traceability
66. CAP-02 capture API slice delivery (`#201`):
   - added authenticated `/api/capture/items` API surface for create/list/detail/ignore/cancel actions with claims-derived user scoping
   - create endpoint now returns `201 Created` and persists capture payloads via queue-wrapper model (`LlmRequest` + `inbox.capture.v1`)
   - list/detail contracts now enforce excerpt-only list payloads and detail-only full text visibility, with idempotent ignore/cancel action behavior and cross-user `403` vs true-missing `404` policy coverage
67. CAP-04 triage enqueue + state transition delivery (`#203`):
   - added authenticated triage enqueue endpoint: `POST /api/capture/items/{id}/triage` returning `202 Accepted`
   - capture triage enqueue now returns deterministic triage state (`Triaging`) with explicit idempotent replay signaling (`AlreadyTriaging`)
   - invalid-state transitions now return stable `Conflict` error-contract payloads, including ignored/cancelled capture items
   - queue processing guardrails now skip pending capture request types (`inbox.capture.v1`) to preserve explicit triage-trigger semantics ahead of CAP-05 worker routing
68. CAP-05 triage worker routing and proposal generation delivery (`#204`):
   - queue worker now routes triaging capture items (`inbox.capture.*` + `Processing`) through a dedicated capture-triage pipeline rather than generic planner parsing
   - deterministic extraction baseline now converts checklist/bullet/numbered capture content into proposal operations with stable idempotency keys
   - triage pipeline now persists provenance linkage (`capture item -> triage run -> proposal`) on capture payloads and exposes `ProposalCreated` capture status once linked
   - capture triage failure paths now return deterministic non-mutating outcomes (no direct board writes), with bounded retry behavior retained under worker policy
69. CAP-06 strict triage contract + prompt versioning delivery (`#205`):
   - added strict triage output contract (`capture-triage-output.v1`) with version + prompt invariants and explicit machine-readable schema file under `Taskdeck.Application/Schemas`
   - triage proposal generation now validates structured output against schema constraints before creating proposals, with deterministic `ValidationError` outcomes on contract violations
   - triage provenance persistence now includes `promptVersion` (`triage.v1`) for each successful triage run (`capture item -> triage run -> proposal`)
   - added deterministic fixture-backed validation coverage (golden + negative cases for missing tasks, wrong prompt version, unknown properties)
70. CAP-07 inbox frontend route/list/detail delivery (`#206`):
   - added workspace inbox surface (`/workspace/inbox`) with shell navigation and router integration
   - inbox list now renders excerpt-first capture summaries, while full raw capture text is fetched only on detail open
   - inbox detail now supports deterministic ignore/cancel actions with refreshed capture state after mutation calls
   - keyboard-first inbox navigation (`ArrowUp`/`ArrowDown`/`Enter`) plus escape-stack compliant detail close behavior is now covered by frontend regression tests
71. CAP-08 capture modal + command palette/hotkey delivery (`#207`):
   - added quick capture modal with keyboard-first submit (`Ctrl+Enter`) and deterministic close behavior
   - command palette now includes explicit capture action command while preserving inbox navigation command access
   - global quick capture hotkey (`Ctrl+Shift+C`) now opens capture modal from workspace shell contexts
   - successful capture submission now routes directly to inbox and surfaces the new item in list state for immediate follow-through
72. CAP-09 inbox triage trigger + proposal-linking UX delivery (`#208`):
   - inbox detail now includes explicit triage enqueue action with deterministic in-progress/completion state handling
   - capture detail contract now surfaces provenance linkage metadata (`capture item -> triage run -> proposal`) for UI consumers
   - inbox detail now renders direct proposal review navigation when triage yields a linked proposal id
   - frontend regression suite now covers triage action success/failure and proposal-link rendering paths
73. CAP-10 card/proposal provenance UX delivery (`#209`):
   - added card provenance API contract for capture-created cards (`GET /api/boards/{boardId}/cards/{cardId}/provenance`) with board-scope authz guardrails (`403` cross-user)
   - capture triage create-card operations now persist deterministic card target ids so provenance lookup remains stable after proposal execution
   - card modal now surfaces capture-origin marker, capture/proposal deep-links, proposal status, and triage-run metadata when provenance exists
   - automations proposal surface now exposes capture-linked context (capture artifact link + triage-run reference), with frontend/backend regression coverage
74. CAP-11 capture loop end-to-end regression delivery (`#210`):
   - added dedicated Playwright regression (`tests/e2e/capture-loop.spec.ts`) for capture create -> triage -> proposal approve/execute -> card provenance verification
   - end-to-end flow now validates proposal-first trust posture by asserting board mutation only after explicit proposal approval and execute action
   - regression asserts resulting card provenance links (`Open Capture`, `Open Proposal`) and triage-run metadata visibility in card modal
   - full Playwright suite now exercises capture-loop path by default to guard against cross-surface regressions
75. CAP-12 canonical docs promotion delivery (`#211`):
   - updated canonical docs (`docs/STATUS.md`, `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/TESTING_GUIDE.md`, `docs/MANUAL_TEST_CHECKLIST.md`) to represent capture MVP as shipped behavior
   - moved capture validation language from planned-only posture to active regression posture in testing and manual guides
   - marked original in-review capture pack READMEs as historical/stale after canonical promotion
76. TST-17 drag/drop persistence regression coverage delivery (`#256`):
   - `tests/e2e/smoke.spec.ts` now asserts card drag/move persistence after a full page reload by validating target-column presence and source-column absence post-refresh
   - `tests/e2e/smoke.spec.ts` now asserts column reorder persistence after a full page reload using explicit ordered heading checks
   - drag-handle safety coverage in smoke was hardened to use stable add-card control coordinates for non-handle drag attempts, reducing intermittent setup flake while preserving behavior assertions
77. AUTO-03 provider-agnostic runtime delivery (`#232`):
   - expanded runtime provider support to `OpenAI` + `Gemini` behind deterministic environment/config gates with explicit `Mock` fallback on invalid live-provider configuration
   - added Gemini provider adapter (`generateContent`) and parity fallback behavior across success/failure/invalid-response/cancellation branches
   - capture triage provenance now persists provider/model metadata (`provider`, `model`) alongside `promptVersion` for linked triage/proposal flows
   - expanded regression coverage across selection policy, provider adapters, capture provenance surfaces, and API chat integration with non-mock provider stubs
   - follow-on managed-key identity attribution baseline (`#236`) now threads server-derived attribution (`userId`, correlation ID, source surface, board/session scope) through chat/provider boundaries, persists attribution in capture provenance, and adds spoofing/propagation regression coverage
78. INT-01 external import adapters foundation delivery (`#75`):
   - added provider-registry external import orchestration (`IExternalImportAdapter`, `IExternalImportService`) so new providers can be added without core import-service rewrite
   - shipped CSV adapter baseline with outreach-contact profile mapping and deterministic dedupe key ordering (`linkedin_url` -> `email` -> normalized `display_name+company`)
   - added board-scoped authenticated import endpoint (`POST /api/boards/{boardId}/imports/external`) with dry-run/apply result contracts (`create/update/skip/conflicts`) and rollback-safe apply behavior
   - added backend regression coverage for malformed CSV, duplicate input handling, deterministic upsert behavior, rollback safety, archived-board rejection behavior, and CSV payload/row guardrails, plus operator-facing mapping guidance in `docs/platform/IMPORT_ADAPTERS_GUIDE.md`
79. INT-02 webhook integration security model delivery (`#76`):
   - added board-scoped outbound webhook subscription and delivery contracts (`POST/GET/PATCH/DELETE /api/boards/{boardId}/webhooks`) with authz-safe ownership and revocation handling
   - added mutation-event queueing and signed webhook dispatch (`X-Taskdeck-Webhook-*` headers) with HTTPS/default host safety checks and localhost gating controls
   - added worker/runtime hardening for atomic claim/reload flow, non-success response retry scheduling, dead-letter terminal handling, and stale-processing recovery
   - added backend regression coverage across domain/application/API/worker/repository webhook paths, including non-success dispatch retry/dead-letter branches
80. API CORS development-origin configurability delivery:
   - API CORS composition now keeps default localhost origins (`http://localhost:5173`, `http://localhost:5174`) as baseline behavior
   - development fallback localhost origins (`http://localhost:4173`, `http://localhost:5001`) are now included so restricted local frontend-port runs remain preflight-safe
   - development runtime now accepts additive allowed origins from configuration key `Cors:DevelopmentAllowedOrigins`
   - API integration coverage now verifies both default-origin allowance and development-configured alternate-origin allowance via deterministic in-memory config overrides
81. OPS-16 deployment/container hardening verification matrix delivery (`#142`):
   - added deployment verification script (`scripts/deploy/Verify-TaskdeckDeploymentHardening.ps1`) covering secret-enforcement validation, reverse-proxy header checks, unauthorized-path checks, and startup/restart/shutdown reliability checks for the compose baseline
   - added explicit pass/fail matrix doc (`docs/ops/DEPLOYMENT_HARDENING_MATRIX.md`) and linked it from deployment/testing docs for deterministic operator execution
   - expanded manual checklist coverage for non-automatable deployment controls (backend exposure posture, edge TLS termination posture, host restart rehearsal expectations)
82. PACK-07 warning-first starter-pack apply UX delivery (`#176`):
   - starter-pack apply conflict contract now includes severity (`blocking`/`warning`) and controller conflict responses now hard-stop only on blocking conflicts
   - starter-pack apply service now marks non-blocking seed-card skip paths as warnings and preserves apply success when only warnings exist
   - starter-pack modal now shows explicit applied/skipped/blocked/warnings outcome summaries with warning-first messaging, and backend/frontend regression coverage now locks warning-vs-blocking behavior
83. TST-18 Playwright frontend port-resolution hardening delivery:
   - frontend E2E config now resolves fallback ports deterministically across Playwright runner and worker imports
   - local runs (server reuse enabled) prefer identity-verified running Taskdeck frontend listeners before bind probes to prevent runner/worker drift (`4173` to `5001`)
   - CI runs (server reuse disabled) prefer bindable ports first so stale listeners do not trigger `url is already used` startup failures
   - fallback port selection now persists first resolution in-process (`TASKDECK_E2E_RESOLVED_FRONTEND_PORT`) so worker config imports do not diverge from runner webServer startup port
   - local Windows E2E gate now re-verifies with `npx playwright test --reporter=line` using fallback path (`5173` -> `4173` -> `5001`)
84. FE-13 local dev server startup hardening delivery:
   - `npm run dev` now launches through a small Vite wrapper that auto-resolves restricted/unavailable local ports with fallback order `5173` -> `4173` -> `5001`
   - wrapper now selects the first bindable candidate port and skips occupied candidates for new Vite processes, preventing strict-port startup failures on stale listeners
   - wrapper now sets strict-port startup semantics by default, avoiding implicit Vite auto-increment drift when a requested port is occupied
   - explicit local overrides remain supported (`--host`, `--port`, `TASKDECK_DEV_PORT`) for reproducible manual debugging
   - manual local flows no longer require one-off fallback command rewrites when `localhost:5173` is blocked with `listen EACCES`
85. OPS-19 container-image frontend dependency-policy unblock follow-through:
   - frontend npm dependency graph now keeps `@microsoft/signalr` on its supported `ws@7.5.10` major line via a vendored local tarball dependency (`ws: file:vendor/ws-7.5.10.tgz`) so container `npm ci` no longer fetches blocked registry tarballs for that version
   - frontend npm dependency graph now uses `p-limit@3.0.2` override (compatible with `p-locate@5`) to remove blocked `yocto-queue-0.1.0` fetches without cross-major override drift
   - refreshed lockfile keeps container `npm ci` deterministic and unblocks `.github/workflows/reusable-container-images.yml` frontend build stage
   - local Docker validation confirms `deploy/docker/frontend.Dockerfile` build-stage `npm ci` and `npm run build` both complete successfully with the override
   - [Superseded by `#761` (dependency-overrides audit): vendor tarball `vendor/ws-7.5.10.tgz` removed; `ws` now declared as `^7.5.10` from the npm registry; `p-limit` override removed — npm naturally resolves `p-limit@3.1.0` (highest in the `^3.0.2` range required by `p-locate@5`); orphaned `COPY vendor/` Dockerfile step removed]
86. OPS-20 role discoverability and permission-guidance delivery (`#179`):
   - ops command permission failures now include current-role context, runnable-template fallback lists, and explicit next-step guidance to verify/request elevated access
   - ops console now surfaces current role and runnable-template discoverability context up front, and restricted template selection now shows explicit role-based warnings before run attempts
   - settings profile surface now includes role and ops-capability summaries, and operator/manual docs now codify the role-assignment workflow used for access elevation requests
87. UX-11 archive lifecycle control refinement (`#177`):
   - board settings lifecycle controls now use one explicit archive/restore action with deterministic confirmation messaging, replacing duplicate archive semantics in the same surface
   - archive workspace now supports hiding archived boards from the default list, explicit hidden-board reveal (`Show Hidden Boards`), and reversible unhide actions for clearer long-tail archive management
   - archive/frontend regression coverage now locks hidden-board visibility filtering behavior while API integration coverage locks archive/restore lifecycle transitions via board update contracts
88. SEC-05 OWASP baseline hardening (`#80`, delivered):
   - added API security-header middleware with explicit baseline headers (`Content-Security-Policy`, `X-Frame-Options`, `X-Content-Type-Options`, `Referrer-Policy`)
   - added environment-aware HSTS behavior (enabled for HTTPS, disabled by default in development unless explicitly configured)
   - added API integration coverage for header presence on success and auth-failure paths, plus HTTPS HSTS emission behavior in non-development hosting
   - published `docs/security/SECURITY_OWASP_BASELINE.md` with CSRF posture, OWASP checklist, and tracked follow-up security gaps
89. SEC-06 API rate-limiting and abuse-protection hardening (`#81`, delivered):
   - added partitioned fixed-window rate limiter policies for auth (`AuthPerIp`), capture create/triage (`CaptureWritePerUser`), and hot/costly paths (`HotPathPerUser`)
   - applied endpoint-level rate-limit policies across auth, capture, chat, and llm-queue write/stream surfaces
   - standardized throttle response contract (`429` + `ApiErrorResponse`) with deterministic retry diagnostics headers (`Retry-After`, `X-RateLimit-Policy`)
   - published operator tuning guidance and safe defaults in `docs/security/RATE_LIMITING_POLICY.md` with regression coverage for burst, reset-window recovery, and cross-user boundary behavior
   - follow-through hardening now supports trusted forwarded-header processing via explicit proxy/network allowlists and configurable forwarded-hop depth (`ForwardedHeaders:ForwardLimit`), while preserving no-trust defaults when allowlists are unset and documenting emergency/rollback plus proxy-topology smoke checks
90. TST-CODEX-01 to TST-CODEX-15 unit test coverage wave (`#415`–`#429`, PRs `#436`–`#448`):
   - added frontend API/composable/store tests and backend domain entity/application service/API tests across 13 PRs
   - adversarial review fixes for tautological assertions, missing guard branches, modifier-key coverage, and edge-case gaps
91. Hotspot refactor and maintenance wave (PRs `#453`–`#456`):
   - AGT-01 follow-up: `AgentRunRepository` now uses pure LINQ (removed `FromSqlInterpolated` raw-SQL SQLite branch)
   - KNOW-01 follow-up: `KnowledgeChunkRepository` uses `ExecuteDeleteAsync`; FTS service uses uppercase GUID comparison, `int?` source-type, application-managed FTS sync via `UpdateFtsIndexAsync`/`DeleteFtsIndexAsync`, and `SanitizeFtsQuery` helper
   - UI-01 follow-up: `design-tokens.css` accent colors DRY-refactored to `--_td-light-accent` variables
   - TST-26 knowledge service test coverage: 32 new backend tests across chunk content, FTS sanitize, authorization, and API integration suites; EF Core migration with proper Designer snapshot; SQLite DateTimeOffset ORDER BY fix; FTS5 trigger-removal migration
92. Security hardening wave (PRs `#457`–`#460`, `#466`):
   - UI-03 primitive stack decision spike: `docs/analysis/ui-primitive-stack-decision-spike.md` selecting shadcn-vue (Reka UI base, copy-paste ownership, WAI-ARIA foundation)
   - DOC-05 / SEC-17 managed-key usage policy: `docs/security/MANAGED_KEY_USAGE_POLICY.md` with fair-use limits, prohibited patterns, and enforcement ladder
   - SEC-10 secrets/config management baseline: `docs/security/SECRETS_MANAGEMENT_BASELINE.md` with secret inventory + rotation runbooks; `deploy/docker-compose.yml` wired with LLM provider env vars
   - SEC-19 incident response runbook + drills: `docs/security/MANAGED_KEY_INCIDENT_RUNBOOK.md` + `scripts/drills/` (5 failure-injection scripts + orchestrator); corrected identity-scope quarantine accuracy in self-review
   - SEC-12 session-token storage hardening: centralized `utils/tokenStorage.ts` abstraction with `isValidJwtStructure` JSON-payload validation; tokenStorage migration across router/sessionStore; CSP `unsafe-inline` removed from `script-src`; session-token ADR at `docs/analysis/session-token-storage-adr.md`
93. Frontend foundations wave (PRs `#461`–`#464`):
   - ActivityView decomposition: ~735 → ~117 lines via `useActivityQuery` + `ActivitySelector` + `ActivityResults`
   - PERF-08 latency budgets: `usePerformanceMark` composable; 16 lazy route imports; `docs/PERFORMANCE_BUDGETS.md` with 7 thresholds
   - BoardView decomposition: ~771 → ~270 lines via `useBoardDragDrop` + `useBoardKeyboardNav` + 4 extracted components; `usePerformanceMark` integrated for board-load instrumentation
   - UI-02 shared primitives foundation: 15 TdButton/TdInput/TdDialog/TdDropdown/TdTooltip/TdBadge/etc. components built on shadcn-vue/Reka UI with WAI-ARIA baseline
94. Feature and security follow-through wave (PRs `#465`–`#471`):
   - OUT-01 JSON manifest import tab: `StarterPackCatalogModal` gains JSON paste/file-upload with validate→dry-run→apply flow
   - StarterPack service decomposition: `StarterPackManifestValidator` split into 4 focused validators/checkers
   - SEC-18 abuse detection operator tooling + domain groundwork: `AbuseActor`/`AbuseEvent` entities, `AbuseDetectionService` with 4-state model; operator evaluation/quarantine API; live-traffic wiring is a follow-up slice
   - ArchiveRecovery decomposition: `ArchiveRecoveryService` → `ArchiveConflictDetector` + `RestorePlanner` + `RestoreExecutor`
   - AutomationExecutor decomposition: `AutomationExecutorService` → `OperationParameterParser` + `ExecutionAuditRecorder` + `OperationHandlerRegistry`
   - Deploy/MCP failure injection drills: 5 shell drill scripts + `run-all-drills.sh` orchestrator in `scripts/drills/`
95. OPS-18 dependency update automation and security triage workflow (`#148`):
   - added `.github/dependabot.yml` with weekly update schedules for NuGet (`/backend`), npm (`/frontend/taskdeck-web`), and GitHub Actions (`/`) ecosystems
   - minor/patch updates grouped per ecosystem; major NuGet/npm updates arrive as individual PRs; GitHub Actions updates fully grouped
   - added `docs/ops/DEPENDENCY_UPDATE_POLICY.md` with update categories, PR verification expectations, severity-based triage SLAs, escalation procedures, and policy boundaries
   - security triage workflow aligns with existing `docs/security/SECURITY_DEPENDENCY_VULNERABILITY_POLICY.md` severity policy; no auto-merge enabled
96. OPS Windows Git resolution hardening (`#121`):
   - added `scripts/check-git-env.sh` diagnostic script validating Git for Windows resolution (not Cygwin/MSYS2) and stale `.git/index.lock` detection with worktree awareness
   - updated `CLAUDE.md` and `AGENTS.md` Windows Notes to reference the script and PATH remediation guidance
97. TST-08 testing and hardening strategy analysis (`#143`):
   - delivered `docs/analysis/2026-03-29_testing-hardening-strategy.md` with gap analysis across backend/frontend tests, CI, MCP, deployment, ops reliability, and security
   - proposed 15 follow-up issues across 4 priority tiers with acceptance criteria and execution sequencing
98. TST-25 headed manual-audit Playwright pack (`#369`):
   - added `frontend/taskdeck-web/tests/e2e/manual-audit.spec.ts` covering core `Home -> Inbox/Capture -> Review -> Board` audit loop with 18 screenshots
   - live LLM probes gated behind `TASKDECK_RUN_LIVE_LLM_TESTS` env var; CI exclusion via `TASKDECK_RUN_AUDIT` env var gate
   - added `docs/testing/MANUAL_AUDIT_PACK.md` documenting usage vs stakeholder demo recorder vs default smoke
99. TST-07 manual validation slice A — workspace shell, board lifecycle, and keyboard UX (`#130`):
   - added `docs/testing/manual-validation-a-workspace-board-ux.md` with 22 step-indexed scenarios (A-01 to A-22)
   - covers auth flows, shell navigation, board lifecycle, column/card/label operations, keyboard UX, escape behavior stack, and Today view
100. TST-08 manual validation slice B — authz policy, cross-user isolation, and API error contracts (`#131`):
    - added `docs/testing/manual-validation-b-authz-contracts.md` with 175 step-indexed checks (B-01 to B-175) covering all 28 controllers
    - two-user fixture setup with curl-based bootstrap script; covers unauthenticated denial, cross-user board isolation, error payload contract verification
101. AppShell premium reskin delivery (PR `#499`):
    - shell sidebar, topbar, command palette, and keyboard help components reskinned from hardcoded Tailwind/rgba values to `--td-*` design token system
    - added focus-visible accessibility rings throughout shell layer and glass morphism effects for visual coherence
    - no behavior changes; purely CSS/token-based styling refactor
102. Board/card surface polish delivery (PR `#501`):
    - board canvas, toolbar, action rail, column lanes, and card components reskinned to design token system
    - standardized card visual states (hover, focus, selected, disabled, dragging) with token-based styling
    - fixed combined selected+focus-visible keyboard nav specificity conflict; replaced hardcoded font sizes with token references
103. AGT-02 tool registry, policy evaluator, and first bounded template delivery (`#337`, PR `#502`):
    - added domain primitives: `ToolScope`/`ToolRiskLevel` enums, `ITaskdeckTool`/`ITaskdeckToolRegistry` interfaces, `PolicyDecision` value object (AllowDirect/AllowWithReview/Deny factories)
    - added `TaskdeckToolRegistry` (thread-safe ConcurrentDictionary, duplicate rejection, scope filtering) and `AgentPolicyEvaluator` (allowlist enforcement, risk-level gating with review-first defaults)
    - added `InboxTriageAssistant` bounded template: gathers pending inbox items, routes through policy evaluator, creates proposals (never direct board mutations)
    - DI registration: singleton tool registry with `inbox.triage` pre-registered, scoped policy evaluator and triage assistant
    - 42 backend tests across registry, policy evaluation, and inbox triage assistant suites
104. Demo director reporting, assertions, presets, and soak mode delivery (`#331`, PR `#500`):
    - added named preset system (`demo-director-presets.mjs`) for common demo modes with override merging and runtime registration
    - added trace assertion utilities (`demo-trace-assertions.mjs`) for exact/structural comparison plus step ordering validation
    - added HTML report generator (`demo-report-html.mjs`) with inline styles, trace tables, pass/fail badges, and embedded base64 screenshots
    - added soak mode (`demo-soak.mjs`) for long-run director scenario loops with configurable iteration counts, cooldown, and cumulative metrics
    - 63 frontend tests covering presets, assertions, reports, soak mode, and integration pipeline
105. Incident rehearsal and recovery program delivery (`#150`, PR `#503`):
    - added `docs/ops/INCIDENT_REHEARSAL_CADENCE.md` with monthly lightweight + quarterly deep drill schedule and rotation model
    - added `docs/ops/EVIDENCE_TEMPLATE.md` for standardized rehearsal outcome format with ISO 8601 timeline and bidirectional issue linking
    - added `docs/ops/REHEARSAL_BACKOFF_RULES.md` with finding-to-issue workflow, severity labels (P1–P4), and SLA expectations
    - added 4 rehearsal scenario templates (degraded-api-health, missing-telemetry-signal, mcp-server-startup-regression, deployment-readiness-failure)
    - added first execution evidence at `docs/ops/rehearsals/2026-03-29_degraded-api-health.md`
    - cross-linked from `TESTING_GUIDE.md` and `MANUAL_TEST_CHECKLIST.md`
106. Chat-to-proposal NLP gap fix delivery (`#570`, PR `#602`):
    - added `NaturalLanguageInstructionExtractor` to bridge intent classification-to-parsing gap (translates natural language into structured instructions the regex parser can consume)
    - all three LLM providers (Mock, OpenAI, Gemini) now use the extractor as fallback when structured JSON extraction fails
    - 38 unit tests for the extractor covering extraction patterns and edge cases
107. Multi-instruction batch parsing delivery (`#574`, PR `#591`):
    - added `ParseBatchInstructionAsync` to `IAutomationPlannerService` for splitting multiple natural-language instructions into individual planner calls
    - `ChatService` now routes multi-instruction messages through batch parsing to generate multiple proposals from a single chat message
    - backend + frontend tests for batch instruction parsing and ChatService integration
108. Board-context LLM prompting delivery (`#575`, PR `#589`):
    - added `BoardContextBuilder` to construct bounded board context (columns, card titles, labels) for LLM system prompts
    - added `LlmSystemPromptBuilder` for centralized system prompt composition across providers
    - OpenAI and Gemini providers now append board context via the builder; backend tests for builder and ChatService integration
109. Board keyboard card movement delivery (`#248`, PR `#590`):
    - added Alt+Arrow keyboard shortcuts for card movement within and across columns via `useBoardKeyboardNav` composable
    - added move-to action menu on CardItem for click-based column moves with Escape handling and focus restoration
    - extracted adjacent-column and reorder helpers from composable; added Card Movement section to keyboard help dialog
    - frontend unit tests for keyboard movement, ColumnLane test prop fix, and coverage expansion
110. Transcript capture source delivery (`#218`, PR `#592`):
    - added `TranscriptFile` capture source with transcript-specific size limits to backend domain
    - added transcript paste/file capture mode to CaptureModal frontend
    - backend validation tests and frontend interaction tests
111. Contact card YAML parser delivery (`#264`, PR `#588`):
    - added `ContactCardYamlParser` with parse/serialize and field validation for card-first outreach CRM
    - added `ContactCardFrontMatter` model with `YamlDotNet` dependency; static serializer/deserializer caching
    - backend unit tests for parser
112. Global search and quick-action launcher delivery (`#93`, PR `#603`):
    - added `SearchService` and `GET /api/search?q=` endpoint for cross-board search respecting authorization boundaries
    - enhanced `ShellCommandPalette` (Ctrl+K) with live search results (boards + cards) alongside command navigation
    - added `searchApi` client, `useGlobalSearch` composable with 200ms debounce and abort-on-supersede
    - frontend tests for composable and command palette search integration
113. Developer portal and OpenAPI delivery (`#99`, PR `#605`):
    - added OpenAPI annotations to 7 controllers (Boards, Cards, Columns, Capture, Chat, Auth, Webhooks) with `[ProducesResponseType]` and XML doc summaries
    - enhanced Swagger configuration with API metadata, JWT Bearer security definition, and XML comment inclusion
    - added developer portal docs (`docs/api/`): `QUICKSTART.md`, `AUTHENTICATION.md`, `BOARDS.md`, `CAPTURE.md`, `CHAT.md`, `WEBHOOKS.md`, `ERROR_CONTRACTS.md`
    - added developer portal CI workflow and local OpenAPI export script
114. SBOM and release provenance delivery (`#103`, PR `#606`):
    - added reusable workflow for CycloneDX JSON SBOMs (backend + frontend) and SLSA v1-style build provenance manifest with SHA-256 checksums
    - wired into `ci-release.yml` (replacing placeholder) and `release-security.yml`
    - added documentation at `docs/ops/SBOM_RELEASE_PROVENANCE.md`; updated dependency vulnerability policy
115. Batch triage and suggestion editing delivery (`#220`, PR `#607`):
    - added `POST /api/capture/items/batch-triage` with per-item actions (triage/ignore/cancel), 200/207/422 response semantics, batch size limit (50), and duplicate ID rejection
    - added `PUT /api/capture/items/{id}/suggestion` for editing capture text before triage with state-transition guards
    - added multi-select checkboxes, select-all toggle, batch action bar, and inline suggestion editing in InboxView
    - backend + frontend tests for batch triage and suggestion editing
116. Property-based and fuzz testing pilot delivery (`#89`, PR `#601`):
    - added FsCheck property-based testing packages to Domain and Application test projects
    - added property-based tests for Board, Card, Column, Label entity invariants and AutomationProposal state machine invariants
    - added fuzz tests for StarterPackManifestValidator input parsing, LlmIntentClassifier regex safety, and export/import DTO serialization roundtrip contracts
117. Accessibility audit and WCAG remediation delivery (`#92`, PR `#604`):
    - added accessibility foundation: skip-to-content link, `sr-only` utility class, `eslint-plugin-vuejs-accessibility` with tuned gradual-rollout rules
    - WCAG improvements across BoardView, HomeView, TodayView, ReviewView, InboxView, CaptureModal, and ToastContainer (ARIA landmarks, roles, labels)
    - added Playwright axe-core E2E tests for 6 core views (Home, Today, Inbox, Review, Boards, Login) plus skip-link verification
    - `role=presentation` on virtual scroller wrappers for axe-core compliance
118. Dependency update wave (PRs `#593`–`#600`):
    - `@eslint/js` 9.39.4 → 10.0.1 (with ESLint v10 rule violation fixes)
    - `@types/node` 24.10.1 → 25.5.0
    - GitHub Actions group bump (5 updates)
    - `Microsoft.NET.Test.Sdk` 17.14.1 → 18.3.0
    - `Swashbuckle.AspNetCore` 6.9.0 → 10.1.7 (with OpenApi v2.x compatibility fix)
    - `Microsoft.IdentityModel.Tokens` and `System.IdentityModel.Tokens.Jwt` upgraded to 8.17.0
    - `xunit.runner.visualstudio` 2.8.2 → 3.1.5
119. LLM tool-calling spike completion (`#618`, 2026-04-01):
    - completed architecture document at `docs/spikes/SPIKE_618_COMPLETED.md` (1,014 lines, 13 sections)
    - decided: custom implementation over Semantic Kernel (~800 LOC, zero new dependencies); SK's Gemini connector is alpha-quality with known function-calling bugs, and SK auto-invokes functions conflicting with GP-06
    - decided: extend `ILlmProvider` with `CompleteWithToolsAsync()` — incremental, no breaking changes to existing non-tool-calling flow
    - decided: 11 tools total (5 read + 6 write); reads execute directly, writes always produce proposals via `propose_*` prefix
    - decided: new `ToolCallingChatOrchestrator` wraps `ChatService` with multi-turn loop (max 5 rounds, 60s total timeout, SignalR intermediate states)
    - decided: Mock provider uses pattern-matching dispatch table for deterministic tool-call simulation
    - cost model: ~$0.00088 per 3-round conversation on GPT-4o-mini (2-3x static context but unlocks dynamic board querying)
    - implementation tracker: `#647`; phase issues: `#649` (read tools + orchestrator), `#650` (write tools + proposals), `#651` (refinements)
120. MCP server spike completion (`#619`, 2026-04-01):
    - completed architecture document at `docs/spikes/SPIKE_619_COMPLETED.md` (1,374 lines, 16 sections + 2 appendices)
    - decided: official MCP C# SDK (`ModelContextProtocol` v1.2.0, co-maintained by Microsoft, 4.2k stars, .NET 8 native)
    - decided: embedded in API process with `--mcp` startup flag for stdio mode; HTTP alongside REST on same Kestrel instance
    - decided: stdio transport first (Claude Code/Cursor local dev), Streamable HTTP added in Phase 3 for cloud/remote
    - decided: 9 resources under `taskdeck://` URI scheme, 9 tools (2 read + 5 write + 2 proposal management); `approve_proposal` intentionally excluded (GP-06)
    - decided: API key auth (`tdsk_` prefix, SHA-256 hashed, user-bound) for HTTP transport; OAuth 2.1 deferred to Phase 4
    - decided: write tools return proposal IDs immediately; users approve in web UI; agents poll via `get_proposal_status`
    - implementation tracker: `#648`; phase issues: `#652` (minimal prototype), `#653` (full inventory), `#654` (HTTP + auth), `#655` (production hardening, deferred)
121. SQL-level board metrics filtering delivery (`#675`/`#724`, 2026-04-03):
    - added dedicated repository methods (`GetForMetricsAsync`, `CountCardsByColumnAsync`, `GetBlockedByBoardIdAsync`) for SQL-level filtering instead of in-memory post-fetch filtering
    - `BoardMetricsService` now delegates filtering to SQL queries for scalability on large boards
    - frontend `Math.max(...spread)` replaced with `reduce` for empty-array safety
122. Double LLM call elimination delivery (`#672`/`#727`, 2026-04-03):
    - `ChatService` now reuses the orchestrator's text response when no tools are called instead of making a second LLM completion request
    - halves latency for non-tool chat messages with no behavior change for tool-calling flows
123. JWT invalidation hardening delivery (`#671`/`#728`, 2026-04-03):
    - added `ActiveUserValidationMiddleware` that checks user active status on every authenticated request with 30-second in-memory cache
    - cache invalidated on user deletion/deactivation so stale JWTs are rejected within seconds
    - complements the `TokenValidationMiddleware` (PR `#698`) with runtime active-user enforcement
124. Expired proposal review UX delivery (`#678`+`#690`/`#729`, 2026-04-03):
    - added `IsExpired` flag on `ProposalDto` and domain `CanBeDismissed` method
    - expired proposals in Review now show distinct "Expired" status badge with dismiss action and explanatory notice
    - Apply/Approve buttons disabled for expired proposals; 60-second reactive clock covers proposals expiring while page is open
125. Infrastructure repository integration tests delivery (`#699`/`#730`, 2026-04-03):
    - added 77 integration tests across 7 repository classes running against real SQLite
    - found and fixed a real `LlmQueueRepository` ordering bug during test development
    - first delivery from the rigorous test expansion wave (`#721`)
126. LLM write tools and proposal integration delivery (`#650`/`#731`, 2026-04-03):
    - added 6 write tool executors (`propose_create`, `propose_move`, `propose_archive`, `propose_update`, `propose_bulk_move`, `propose_create_column`) in Application layer
    - added EF migration for `ToolCallMetadataJson` field on proposals for tool-call provenance
    - orchestrator now serves 11 tools (5 read + 6 write); writes always produce proposals per GP-06
    - frontend tool-status indicators show write-tool progress via SignalR `ToolStatusEvent`
127. Rigorous test expansion wave 2 delivery (PRs `#740`–`#755`, 2026-04-04):
    - 8 issues from `#721` tracker, ~586 new tests with two rounds of adversarial review (47 review-fix commits)
    - domain entity state machine exhaustive tests (`#701`/`#740`): 174 tests across 7 entities (CommandRun, ArchiveItem, ChatSession, UserPreference, NotificationPreference, CardLabel, CardCommentMention)
    - SignalR hub and realtime integration tests (`#706`/`#751`): 19 tests covering auth, presence, multi-user, authorization, edge cases; review fixed false-positive auth tests and resource leaks
    - LLM provider and tool-calling edge cases (`#709`/`#747`): 101 tests across orchestrator, provider, classifier, registry; review added loop detection and registry edge cases
    - data export/import round-trip integrity tests (`#713`/`#752`): 64 tests covering JSON, CSV, GDPR, database, cross-format validation
    - API error contract regression tests (`#714`/`#753`): 57 tests across 7 endpoint families with GP-03 contract enforcement; review fixed 12 weak 404 assertions and 2 false-positive contract tests
    - archive and restore lifecycle tests (`#715`/`#755`): 74 tests (45 domain + 29 API) covering state machine, cross-user isolation, conflict detection, audit trail
    - board metrics accuracy verification tests (`#718`/`#749`): 61 tests (51 service + 10 controller) for throughput, cycle time, WIP, blocked cards, done-column heuristic
    - notification delivery integration tests (`#719`/`#746`): 36 tests covering all 5 notification types, deduplication, preference filtering, cross-user isolation, batch operations
    - wave progress: 15 of 22 `#721` issues now delivered (~886 new tests total); 7 issues remain open
128. Post-adversarial-review hardening and test expansion (PRs `#741`–`#756`, 2026-04-04):
    - 9 issues from `#721` tracker plus product telemetry taxonomy, two bug fixes, and six frontend regression test additions
    - product telemetry taxonomy delivered (`#341`/`#741`): `docs/product/TELEMETRY_TAXONOMY.md` with 35+ named events, privacy-first bucketing, and R1/R2/R3 launch gate anchors; opt-in, not yet implemented
    - board header presence label bug fixed (`#683`/`#744`): username/email flip resolved with `normalizePresenceMembers()` in `BoardView.vue`; adversarial review confirmed no edge cases; 3 new tests
    - manual card provenance empty state fixed (`#680`/`#754`): 3 bugs caught and fixed by adversarial review (overly broad 404 swallow, global Axios log regression, empty-state flash); `CardModal.vue` now shows "No capture provenance available." correctly; 4 new tests
    - WIP-limit toast dedup regression tests (`#686`/`#745`): 7 tests in `boardStore.wipLimit.spec.ts` for `createCard` and `moveCard`
    - auth-flow toast lifecycle tests (`#685`/`#742`): 20 tests in `sessionStore.authToast.spec.ts`; adversarial review fixed timer leak, mock isolation, inverted assertion
    - router auth guard + workspace stability tests (`#687`/`#748`): `authGuard.spec.ts` and `workspaceRouteStability.spec.ts` with 16-case exhaustive guard table; pre-existing `AuthControllerEdgeCaseTests.cs` compile error fixed
    - inbox triage action visibility tests (`#688`/`#743`): 21 new tests in `InboxView.spec.ts` for single-item triage and bulk action bar visibility
    - webhook HMAC verification tests (`#726`/`#750`): 11 tests in `OutboundWebhookHmacDeliveryTests.cs` for header format, round-trip, wrong-key, secret rotation, timing-safe comparison
    - webhook delivery reliability + SSRF boundary tests (`#710`/`#756`): 78 total webhook tests across 9 files; SSRF coverage via `OutboundWebhookEndpointGuardTests` for private IP ranges; retry/backoff/dead-letter reliability; `HttpClient` resource leak fixed in tests
    - TST-32–TST-57 wave progress updated: 17 of 25 issues now delivered; remaining open: `#705`, `#711`, `#712`, `#716`, `#717`, `#720`, `#723`, `#725`; frontend suite at 1592 passing (up from 1496)
129. Dependency hygiene, accessibility, tool-calling refinements, streaming, and test coverage wave (PRs `#771`–`#779`, 2026-04-04):
    - vendored dependency cleanup (`#761`/`#771`): removed `vendor/ws-7.5.10.tgz` and orphaned Dockerfile `COPY vendor/` line; `ws` resolves from registry as `^7.5.10`; no-op `p-limit` override removed; adversarial review updated stale STATUS.md/MASTERPLAN docs references
    - accessibility lint remediation (`#762`/`#779`): 105 warnings → 0; form label associations, keyboard event companions, ARIA modal/backdrop attributes, `--max-warnings 20` CI threshold; adversarial review fixed 2 CI regressions (TdTooltip Fragment, role="option" tabindex violation); 2 non-blocking ARIA follow-up items filed
    - tool-calling Phase 3 refinements (`#651`/`#773`): `LlmToolCallingSettings` with `Enabled`/`MaxToolResultBytes` config keys; `ChatService` bypasses orchestrator when disabled; `TruncateToolResult` binary-search UTF-8 byte budget; cost tracking DI wiring completed; 17 new tests (2 added by adversarial review fixing byte-budget contract bug and replacing O(n) loop)
    - export streaming (`#670`/`#774`): `GET /api/account/export/stream` streams via `Utf8JsonWriter`; `CountBySessionIdsAsync` GROUP BY fixes N+1; 500-session batch respects SQLite 999-param limit; 15 tests; adversarial review fixed `ToErrorActionResult()` crash after `Response.HasStarted`
    - frontend view vitest coverage (`#716`/`#775`): 83 tests across 6 views (LoginView, RegisterView, BoardsListView, ExportImportView, SavedViewsView, DevToolsView); adversarial review fixed 3 ESLint errors (CI blocker) and added 3 OAuth callback path tests
    - Pinia store integration tests (`#711`/`#777`): 91 tests across 6 stores mocking HTTP layer; covers #508/#509 regressions; adversarial review fixed timer leak, microtask drain, and 4 type-bypass casts
    - resilience/degraded-mode tests (`#720`/`#778`): 34 tests (18 backend + 16 frontend); adversarial review fixed CI blocker (unused import), double-invocation anti-pattern, and timing race
    - E2E error state expansion (`#712`/`#772`): 25 Playwright scenarios across 3 spec files using `page.route()` interception; adversarial review fixed CI blocker (unused import), route glob, and 3 vacuous assertions
    - TST-32–TST-57 wave: 23 of 25 issues now delivered (added `#723`/`#769` and `#725`/`#765` from parallel wave); remaining open: `#705`, `#717`; frontend suite ~1734 passing
130. SQLite-to-PostgreSQL production migration strategy (`#84`, 2026-04-09):
    - ADR-0023: recommends PostgreSQL as target production DB provider; documents alternatives (SQL Server, CockroachDB, MySQL) and tradeoffs; SQLite retained for local dev and CI
    - migration runbook at `docs/platform/SQLITE_TO_POSTGRES_MIGRATION_RUNBOOK.md`: schema export via EF Core migrations, CSV-based data export/import in dependency order, row-count and foreign-key integrity verification, smoke test checklist, rollback procedure, security considerations
    - 20-test provider-compatibility harness (`DatabaseProviderCompatibilityTests`): CRUD on Board/Card/Proposal, DateTimeOffset fidelity, GUID storage and FK joins, string collation, ordering, pagination, enum storage, aggregates, boolean filtering, concurrent inserts, Unicode; documents SQLite `DateTimeOffset` ORDER BY limitation
    - PostgreSQL tests opt-in via `TASKDECK_TEST_POSTGRES_CONNECTION` environment variable (future CI integration)

130. Platform expansion wave delivery (PRs `#796`–`#805`, 2026-04-09):
    - 10 parallel worktree agents delivered platform hardening, testing infrastructure, ops documentation, and PWA readiness with two rounds of adversarial review per PR (22 CRITICAL + 32 HIGH findings caught and resolved)
    - **PLAT-01** SQLite-to-PostgreSQL migration strategy (`#84`/`#801`): ADR-0023 (PostgreSQL target), migration runbook, 20 provider compatibility tests; review caught phantom table, 5 missing tables, FTS5 crash
    - **PLAT-02** Distributed caching (`#85`/`#805`): ADR-0024 (cache-aside), `ICacheService` with Redis/InMemory/NoOp implementations, board list caching, 32 tests; review removed unsafe board-detail cache, fixed permanent Redis disable
    - **PLAT-03** SignalR scale-out (`#105`/`#803`): ADR-0025 (Redis backplane), conditional `AddTaskdeckSignalR`, health check, runbook, 14 tests; review fixed per-probe connection creation, thread-unsafe fields
    - **TST-02** Cross-browser E2E matrix (`#87`/`#800`): Firefox/WebKit/mobile projects, tagging strategy, 9 tests, CI workflows, flaky test policy; review fixed CI gate timeout, extracted shared helpers
    - **TST-03** Visual regression harness (`#88`/`#797`): Playwright `toHaveScreenshot()`, 7 visual tests, CI artifact upload, policy doc; review fixed wrong placeholder (guaranteed test failures), double extensions
    - **TST-05** Mutation testing pilot (`#90`/`#796`): Stryker.NET + Stryker JS configs, weekly CI workflow (non-blocking), policy doc; review removed broken schema URL, invalid properties
    - **TST-06** Ephemeral DBs via Testcontainers (`#91`/`#804`): `Taskdeck.Integration.Tests` project, PostgreSQL containers, per-test isolation, 20 tests, Docker skip; review fixed DbContext race condition, deadlock
    - **UX-09** PWA/offline readiness (`#95`/`#802`): VitePWA, service worker, `useOnlineStatus` composable, offline banner, SW update prompt, 18 tests; review eliminated double-reload race, fixed misleading text
    - **OPS-12** Cloud cost observability (`#104`/`#798`): ADR-0026, cost framework, hotspot registry, breach runbook; review fixed phantom config keys, wrong API endpoints
    - **OPS-14** Cloud topology ADR (`#111`/`#799`): ADR-0027 (ECS Fargate), autoscaling policy, SLO targets, ~$147-152/month estimate, reference architecture; review fixed cost inconsistency, missing worker, health check accuracy
    - ADR numbering: PRs originally all created ADR-0023; canonical numbering is ADR-0023 (PLAT-01) through ADR-0027 (OPS-14); file renames needed during merge

## Current Planning Pivot (2026-03-07)

The 2026-03-06 MVP expansion review packages change the next-cycle emphasis without invalidating the current architecture.

Key conclusion:

- Taskdeck's main near-horizon gap is product legibility, not missing backend capability.
- The demo/tooling layer is now strong enough that the next cycle should focus on making the product teach itself.
- One core system can support three presentation modes (`guided`, `workbench`, `agent`), but only the first two should drive near-horizon execution.

Operational planning rules from this pivot:

1. Prioritize novice-first shell work before broader autonomy, knowledge, or connector breadth.
2. Keep the board as the execution center and make board context travel across capture, review, chat, notifications, and follow-through actions.
3. Treat `Review` as the main automation surface for normal users; keep queue and ops explicitly advanced.
4. Reuse existing backlog items where overlap is real (`#96`, `#93`, `#100`, `#216`, `#77`, `#75`, `#97`, `#98`, `#218`, `#219`) instead of duplicating scope.
5. Keep the seeded productization wave (`#318`, `#320`, `#322`, `#324`, `#326`, `#96`, `#100`, `#328`) synchronized in `#107` before promoting more disconnected UX or future-breadth items.

Decision rules promoted from the expanded blueprint:

- If a feature makes demos better but makes the product harder to understand, it is not done.
- If a normal happy path depends on raw internal IDs, it is not novice-ready.
- If a page is empty and offers no next step, it is incomplete.
- If an agent action cannot be traced to a run, policy posture, and proposal/artifact outcome, it is not ready.
- Do not let chat-first or disconnected agent-database thinking replace the board/capture/review product core.

Implementation carry-forward from the full source audit:

- treat workspace mode as durable product state; do not let it collapse into local-only view toggles once server-backed preferences become practical
- prefer aggregated product-shaped APIs for `Home`, `Today`, `Review`, and board summary needs over client-side fetch fan-out
- keep proposal summary generation in the application layer instead of forcing the frontend to reverse-engineer meaning from low-level operations
- keep the one-core-three-surfaces navigation contract explicit:
  - guided primary: `Home`, `Today`, `Inbox`, `Projects`, `Review`, `Settings`
  - workbench primary: `Home`, `Projects`, `Inbox`, `Review`, `Automations`, `Activity`, `Notifications`, `Settings`
  - agent primary: `Home`, `Agents`, `Runs`, `Knowledge`, `Inbox`, `Projects`, `Review`, `Integrations`, `Settings`
- preserve product-facing route aliases such as `/workspace/home`, `/workspace/today`, `/workspace/projects`, and `/workspace/review` even when the old implementation-shaped routes remain valid
- keep novice vocabulary explicit in guided surfaces: `Project`, `Review`, and `Inbox` should lead; queue and ops stay clearly advanced
- keep board-aware action-rail behavior explicit (`Capture here`, `Ask assistant`, `Review proposals`, `Add card`) so board context actually travels
- require action-state empty/help states and plain-language top boxes on advanced pages; no page should leave the user with no next step
- avoid orphan surfaces: board, inbox item, proposal, notification, and later agent-run views should deep-link to the related next action or affected entity
- hold the frontend to a minimum polish bar: visible keyboard focus, modal focus trap, listbox aria state, explicit destructive confirmations, and no hover-only critical affordances
- keep first-class backend contracts explicit for Wave P and Wave R:
  - `UserPreference` server state for workspace mode/onboarding/default board
  - aggregate DTOs such as `WorkspaceHomeDto`, `TodayAgendaDto`, `ReviewSummaryDto`, `BoardSummaryDto`
  - `IProposalSummaryService`
  - later `ITaskdeckTool`, `ITaskdeckToolRegistry`, and `IAgentPolicyEvaluator`
- the secondary follow-through set from the audit is now seeded as `#329` to `#334`; keep it below Wave P and reuse anchors such as `#216`, `#77`, `#93`, `#98`, `#311`, `#75`, `#218`, and `#219` instead of duplicating their scope
- the remaining expanded-blueprint architecture wave is now seeded as `#335` to `#341`; keep it below Wave Q and reuse anchors such as `#75`, `#77`, `#98`, `#100`, `#216`, `#218`, `#219`, and `#328` instead of stretching Wave P issues beyond their productization purpose

## Roadmap by Horizon

### Horizon A (Week 1 to 2): Novice-First Shell and Entry Clarity

Focus:
- add workspace mode preference (`guided`, `workbench`, `agent`) and persist it as durable product state
- add a true start surface (`Home`) instead of dropping every user into an implementation-shaped boards list
- make the guided shell contract concrete: `Home`, `Today`, `Inbox`, `Projects`, `Review`, `Settings`, with notifications/archive/help secondary and operator surfaces hidden by default
- make `Review` the primary normal-user automation surface and keep queue explicitly advanced
- replace dead-end empty states with action-oriented help blocks on primary pages
- replace raw board-ID happy paths with selectors/pickers in common flows
- prefer aggregate/product-shaped APIs for shell summaries instead of client-side stitching
- make `Home` product-shaped rather than dashboard-shaped:
  - thesis/welcome line
  - start-here CTAs
  - needs-attention counts
  - continue-working/resume context
  - learn-Taskdeck cards

Exit Criteria:
- a guided-mode user lands on a product-shaped entry surface
- the UI tells the user what to do first without requiring internal docs
- common capture/review/project flows do not require raw IDs
- queue remains available for power users but is no longer the implied default

### Horizon B (Week 3 to 6): Board-Centered Daily Workflow

Focus:
- shipped in `#324`: `Today` as a compact daily agenda surface
- shipped in `#324`: first-run onboarding checklist and first useful board creation wizard
- add proposal summary service and readable proposal cards with plain-language summaries, risk, and deep links
- add board action rails so capture/chat/review follow the current board context by default (`Capture here`, `Ask assistant`, `Review proposals`, `Add card`)
- strengthen deep links across inbox, review, notifications, activity, and resulting boards/cards
- shipped `Today` utility now covers:
  - due today / overdue
  - blocked
  - proposals waiting review
  - inbox needing triage
  - resume point
- remaining follow-through for this horizon:
  - richer contextual help and in-product teaching on top of the shipped board-centered loop
  - broader telemetry and release-gate follow-through beyond the shipped first-run guardrail

Exit Criteria:
- the `capture -> review -> board` loop is visible and coherent inside the product
- board context travels without manual re-entry across primary surfaces
- a first-time user can create first value without wandering through operator pages
- proposal review feels like a product surface, not just a diff viewer

Current status:
- `#326` is now delivered:
  - application-layer proposal presentation now feeds readable review cards with plain-language summaries, impact/risk/source cues, and affected-entity headlines
  - board pages now expose an explicit action rail (`Capture here`, `Ask assistant`, `Review proposals`, `Add card`)
  - board context now travels through inbox, review, chat, notifications, and provenance/deep-link follow-through

### Horizon C (Week 6 to 8): Docs, Help, and Verification Coherence

Focus:
- add a bridge doc (`START_HERE`) for first-run product understanding
- reshape the manual and index around top-level navigation and user goals
- keep `START_HERE.md` and `USER_MANUAL.md` at `docs/` root, while chaptered manual guidance lives under `docs/manual/` and reusable workflow/help-center guides live under `docs/product/`
- required first-run golden-path smoke test, expressed as a deterministic Playwright guardrail
- define product-shaped telemetry and launch criteria for novice beta and later agent alpha
- treat the staged `novice-first-first-run` scenario shape as the acceptance contract for the shipped first-run smoke path
- keep demo tooling as evidence and acceptance support rather than the main onboarding path

Exit Criteria:
- docs entry points match the product's intended top-level navigation
- the first-run smoke path is `Home -> capture -> review -> execute -> board`
- novice users can recover from empty/confusing surfaces without leaving the product context
- launch criteria are explicit enough to guide seeding and release decisions

### Horizon D (Post-R1): Agent Substrate Foundation

Focus:
- add `AgentProfile`, `AgentRun`, and `AgentRunEvent` as first-class runtime primitives
- ~~add a tool registry abstraction and policy evaluator~~ (delivered in AGT-02, `#337`)
- ~~add a first bounded agent template~~ (delivered: `InboxTriageAssistant` in AGT-02)
- add inspectable run traces
- expose agent mode views only after the substrate is real

Current status:
- tool registry, policy evaluator, and first bounded template are now delivered (`#337`): `ITaskdeckTool`/`ITaskdeckToolRegistry` domain interfaces, `AgentPolicyEvaluator` with allowlist + risk-level gating, and `InboxTriageAssistant` bounded template (proposal-only, review-first default)
- LLM tool-calling architecture spike completed (`#618`); Phase 1 delivered (`#649`): read tools + orchestrator + provider tool-calling extension; `#674` delivered (OpenAI strict mode + loop detection with error-retry bypass, PR `#694`); `#677` delivered (card ID prefix resolution for chat-to-proposal continuity, PR `#695`); `#650` delivered (write tools + proposal integration, PR `#731`); `#672` delivered (double LLM call elimination, PR `#727`); `#651` delivered (Phase 3 refinements: cost tracking, `LlmToolCalling:Enabled` feature flag, `TruncateToolResult` byte budget with binary search — 17 new tests, PR `#773`); ~~`#673`~~ delivered (argument replay — `Arguments` field on `ToolCallResult`, OpenAI/Gemini replay uses real arguments, 6 new tests, PR `#770`)
- MCP server architecture spike completed (`#619`); Phase 1 delivered (`#652`/`#664`): minimal prototype with `taskdeck://boards` resource over stdio; ~~`#653`~~ delivered (full inventory — 9 resources + 11 tools, PR `#739`); remaining: `#654` (HTTP + auth), `#655` (production hardening, deferred)
- remaining work: `AgentProfile`/`AgentRun`/`AgentRunEvent` runtime primitives (`#336`), agent mode surfaces (`#338`), inspectable run detail

Exit Criteria:
- runs are first-class and inspectable
- agent behavior remains proposal-first and trace-first by default
- no opaque or silent autonomy is introduced
- LLM chat can dynamically query and mutate board state through tool calls (proposal-first for writes)
- external AI agents (Claude Code, Cursor) can access Taskdeck via MCP (proposal-first for writes)

### Horizon E (Post-R2): Knowledge and Integrations Surface

Focus:
- add local-first knowledge documents/notes and SQLite FTS-backed search
- add note/transcript/clip-style intake paths that feed capture or knowledge flows
- add integrations registry/management view so imports and webhooks have a coherent home
- keep connector behavior capture-first and review-safe by default

Exit Criteria:
- durable searchable context exists without external vector infrastructure
- integrations surface is coherent and discoverable without bypassing review-first rules
- knowledge and connector work builds on the same board/capture/proposal substrate

### Horizon F (Concurrent Foundation Streams)

These continue in parallel where they protect trust, performance, or operator posture, but they should not outrun Horizon A through C product legibility work:

- managed-key LLM control plane and abuse controls: `#235`, `#237` (pending), `#238` (operator tooling groundwork delivered; live-traffic wiring pending), `#239` (delivered), `#240` (delivered)
- premium UI foundations and reskin wave: `#242` to `#250` (plus optional `#251`); foundations delivered: `#243` UI-02 shared primitives, `#245` UI-03 stack spike, `#250` PERF-08 budgets; appshell reskin (`#499`) and board/card polish (`#501`) now shipped with design-token-based styling; UX feedback wave 1 (`#628`) delivered: sidebar footer pinned (`#623`), card drag layout shift eliminated (`#621`), starter-pack modal migrated to design tokens (`#612`), capture triage error messages (`#615`), review collapsible sections with risk color-coding (`#626`); wave 2 delivered: capture triage delimiters (`#614`), chat truncation (`#616`), notification type differentiation/grouping/batch actions (`#625`), search pagination (`#610`), CI-extended path triggers (`#608`); hardening wave (2026-04-03) delivered: label manager dark theme (`#684`), human-readable proposal diffs (`#682`), expired proposal handling (`#678`+`#690`), chat health banner three-state (`#679`), dead workspace routes fixed (`#681`)
- long-list responsiveness and related UX scale follow-through: `#213` (delivered — inbox + activity virtualized; board cards deferred due to drag-and-drop conflicts)
- platform, ops, testing, and maturity backlog: `#84` to `#111`, `#87` to `#91`
- deferred outreach CRM expansion: `#262` to `#268`

## Release Framing

### Platform Release Plan (2026-03-29)

The release plan now spans packaging, cloud, mobile, and collaboration — not just feature milestones.
Strategy documents: `docs/strategy/00_MASTER_STRATEGY.md` and companion pillar docs.
Master tracker: `#531`.

- `v0.1.0` **First Light** (target: Week 1-2):
  - P0 blocker fixes (`#508`, `#509`)
  - self-contained single-file executable (Windows + Linux + macOS)
  - auto-config (JWT, DB path, browser launch)
  - GitHub Release with cross-platform downloads
  - polished README with demo GIF
  - 90-second demo video
  - packaging wave: `#532` → `#533`, `#534`, `#535`, `#536`
  - GTM wave: `#544` → `#545`, `#546`

- `v0.2.0` **Open Doors** (target: Week 3-5):
  - hosted cloud instance on Railway/Render (`#537` → `#538`)
  - GitHub OAuth login (`#539` — delivered)
  - custom domain and TLS
  - Show HN, Reddit, Dev.to launch
  - landing page on custom domain

- `v0.3.0` **In Your Pocket** (target: Week 6-9):
  - PWA manifest + service worker (`#540` → `#541`, `#542`)
  - mobile-responsive CSS for core flows (`#543`)
  - bottom tab navigation for mobile
  - touch-optimized capture modal
  - mobile board view (card list)
  - web push notifications

- `v0.4.0` **Bring Friends** (target: Week 10-14):
  - board sharing with permission levels
  - workspace invitations
  - email notification delivery
  - activity feed per board
  - LLM tool-calling for chat (`#647`: ~~`#649`~~ delivered → ~~`#650`~~ delivered → ~~`#651`~~ delivered)
  - MCP server for external agent integration (`#648`: ~~`#652`~~ delivered → `#653`→`#654`)

- `v0.5.0` **Power Up** (target: Week 15-20):
  - platform installers (Inno Setup, DMG, AppImage)
  - package manager listings (winget, Homebrew, Snap)
  - Google Play listing (TWA/Capacitor)
  - PostgreSQL backend option for cloud
  - free/pro tier limits and billing

- `v1.0.0` **Generally Available** (target: Month 6-8):
  - Apple App Store listing (via Capacitor)
  - workspace/team/organization model
  - local + cloud sync (API-based)
  - optional Tauri 2.0 native desktop shell
  - agent substrate (inspectable runs, bounded templates)

### Feature Milestones (Original)

- `R1` novice-first beta (largely delivered — maps to v0.1.0/v0.2.0):
  - `Home`, `Today`, `Review`, onboarding/help coherence
  - readable proposals, board-centered action rails
  - no raw-ID requirements in common flows
- `R2` agent foundation alpha (maps to v1.0.0+):
  - `AgentProfile`, `AgentRun`, `AgentRunEvent`
  - tool registry and policy evaluator (delivered in AGT-02)
  - first bounded template (delivered: `InboxTriageAssistant`)
  - inspectable run detail
- `R3` knowledge/integrations alpha (post-v1.0.0):
  - `KnowledgeDocument` / `KnowledgeChunk`
  - SQLite FTS search
  - integrations registry
  - at least two meaningful supervised inbound context/capture paths

## Active Backlog (Priority-Labeled)

### Priority I (Current Phase 4 Completion Path)

- **Security bug**: `#722` (SEC-20) — `ChangePassword` does not verify caller identity; any authenticated user can change another user's password. Discovered during 2026-04-03 test audit. Must be resolved before external onboarding.
- Security and policy convergence: `#33`, `#34`, `#44`
- Final cross-user policy convergence follow-through: `#152`
- Starter packs foundation: `#48`, `#49`, `#50`, `#51` (delivered)
- Tech-debt blockers for stable expansion: `#52` (delivered), `#53` (delivered), `#54` (delivered)

### Priority II (Immediate Post-Phase-4 Foundation)

- Analysis follow-through wave tracker: `#151`
- Capture realignment wave: `#199` to `#211` (delivered); logging redaction follow-through `#212` is delivered, and remaining linked performance follow-through is `#213`
- Testing harness guardrails wave (`#254` to `#260`) is delivered; follow-up improvements now route through normal hardening issues
- Rigorous test expansion wave (`#721` tracker, `#699`–`#720`, `#722`–`#726`): 22 issues seeded 2026-04-03 from systematic codebase audit covering infrastructure repository integration tests, untested workers, controller HTTP gaps, cross-user data isolation proof, concurrency stress, auth edge cases, domain state machines, SignalR hub integration, proposal lifecycle edge cases, LLM tool-calling boundaries, webhook SSRF, frontend store/view gaps, E2E scenarios, export/import round-trips, error contracts, resilience testing, and property-based/adversarial input testing; golden path integration test (`#703`) is highest-signal individual item; first delivery: ~~`#699`~~ infrastructure repo integration tests (77 tests, 7 classes, PR `#730`)
- Provider-agnostic LLM runtime expansion (`OpenAI` + `Gemini`) and demo setup hardening: `#232` (delivered)
- Managed-key LLM control-plane tracker and foundations: `#235`, `#236` (delivered), `#237`
- CI/workflow topology expansion and governance track: `#168`
- API/frontend hardening follow-through: `#153` (delivered), `#154` (delivered), `#155` (delivered), `#157` (delivered)
- Real-time and observability baseline: `#67` (delivered), `#68` (delivered)
- Container/deployment and performance harness baseline: `#69` (delivered), `#70` (delivered), `#142` (delivered)
- Multi-tenancy strategy and collaboration/integration foundations: `#71` (delivered), `#72` (delivered), `#73`, `#74`, `#75`, `#76` (delivered)
- Seeded Wave P from the 2026-03-07 MVP expansion integration:
  - `#318` tracker
  - `#320` workspace modes + `Home` summary shell (delivered)
  - `#322` `Review`-first routing + empty/help states + board selectors (delivered)
  - `#324` `Today` agenda + onboarding path (delivered)
  - `#326` proposal readability + board-centered action flow (delivered)
  - `#96` onboarding/contextual help (delivered)
  - `#100` user guides/tutorials/FAQ (delivered)
  - `#328` first-run smoke + launch-criteria guardrail (delivered)
- Seeded Saul-facing demo alignment wave:
  - `#356` tracker
  - `#354` client-onboarding starter pack + deterministic hero scenario
  - demo-critical `#326` trust-first readability hardening
  - demo-critical `#330` hero-path/demo-board cue hardening
  - `#355` rehearsal contract + acceptance checklist (delivered)
  - `#216` broader reusable demo script/public framing (current execution step)
- Reuse-before-duplicate anchors for this wave:
  - `#326` proposal readability and trust cues
  - `#330` in-app demoability and hero-board quality
  - `#216` demo script / public framing
  - `#175` broader starter-pack expansion after the narrow pre-demo slice
- Related but intentionally not folded into Wave P core execution: `#93`, `#216`, `#77`

### Priority III (Expansion Tranche: Analytics, Security, Compliance, Premium UI Foundations)

- Analytics and forecasting: `#77` (delivered — board metrics dashboard, PR `#667`; SQL-level filtering follow-up ~~`#675`~~ delivered, PR `#724`), `#78`, `#79`
- Security/compliance expansion: `#80` (delivered), `#81` (delivered; capture scope extended), `#82`, `#83` (delivered — GDPR data portability + account deletion, PR `#666`; follow-ups `#670`, ~~`#671`~~ (delivered — JWT invalidation after account deletion, PRs `#698`+`#728`, ADR-0021)), `#106`, `#110` (SEC-10 delivered), `#156`, `#212` (delivered), `#238` (SEC-18 operator tooling + groundwork delivered; live wiring follow-up pending), `#239` (SEC-19 delivered), `#240` (delivered)
- Frontend premium UI foundations wave: `#242`, `#243` (UI-02 shared primitives delivered), `#244`, `#245` (UI-03 stack spike delivered), `#246`, `#247`, `#248`, `#249`, `#250` (PERF-08 delivered)
- Frontend premium wave reused dependencies: `#154` (lint/CI), `#88` (visual regression), `#92` (a11y remediation), `#213` (virtualization)
- Seeded secondary MVP follow-through wave (lower priority than Wave P):
  - `#329` tracker
  - `#330` in-app demoability and live attention cues
  - `#331` demo director reporting/assertions/presets/soak (delivered)
  - `#332` replay-from-trace and scenario-authoring follow-through
- Seeded expanded-blueprint architecture wave (future agent/knowledge/release-gate follow-through):
  - `#335` tracker
  - `#336` agent profile/run/event foundation
  - `#337` tool registry, policy evaluator, and first bounded template (delivered)
  - `#339` knowledge document + SQLite FTS foundation
- Reuse-before-duplicate anchors for this later wave: `#75`, ~~`#77` (delivered — board metrics dashboard, PR `#667`)~~, `#98`, `#100`, `#216`, `#218`, `#219`, `#328`
- LLM tool-calling implementation wave (from completed spike `#618`):
  - `#647` tracker
  - ~~`#649` Phase 1: read tools + orchestrator + provider tool-calling extension~~ (delivered 2026-04-01, PR `#669`)
  - ~~`#650` Phase 2: write tools + proposal integration~~ (delivered 2026-04-03, PR `#731`)
  - ~~`#651` Phase 3: refinements — cost tracking, feature flag~~ (delivered 2026-04-04): `LlmToolCalling:Enabled` feature flag, `TruncateToolResult` token budget enforcement, cost tracking to `ILlmQuotaService`, 15 new tests; also ~~`#672`~~ (double LLM call — delivered 2026-04-03, PR `#727`), `#673` (argument replay); ~~`#674`~~ (strict mode + loop detection — delivered 2026-04-03, PR `#694`)
  - Dependency chain: ~~`#649`~~ → ~~`#650`~~ → ~~`#651`~~
  - Unblocks conversational refinement (`#576`) and MCP tool inventory (`#653`)
- MCP server implementation wave (from completed spike `#619`):
  - `#648` tracker
  - ~~`#652` Phase 1: minimal prototype — one resource + stdio + Claude Code~~ (delivered 2026-04-01, PR `#664`)
  - `#653` Phase 2: full resource + tool inventory (2-3 weeks)
  - `#654` Phase 3: HTTP transport + API key auth (1-2 weeks)
  - `#655` Phase 4: production hardening (deferred to v0.4.0+ demand, `Priority IV`)
  - Dependency chain: ~~`#652`~~ → `#653` → `#654` → `#655`
  - Phase 2 mirrors LLM tool-calling tool abstractions; shared Application layer services

### Platform Expansion Wave (2026-03-29 — Priority II)

Seeded from `docs/strategy/00_MASTER_STRATEGY.md` and companion pillar documents.

- Master strategy tracker: `#531`
- Packaging and distribution wave: `#532` → `#533` (SPA serving), `#534` (build script), `#535` (release workflow), `#536` (first-run config)
- Cloud and collaboration wave: `#537` → `#538` (cloud deploy), ~~`#539` (GitHub OAuth — delivered, PR `#668`)~~; follow-up: `#676` (distributed auth code store, PKCE, account linking)
- Mobile platform wave: `#540` → `#541` (PWA manifest), `#542` (service worker), `#543` (mobile responsive)
- Market adoption and GTM wave: `#544` → `#545` (README polish), `#546` (demo video), `#547` (LICENSE)
- Cross-cutting: `#548` (legal/privacy), `#549` (analytics/error tracking), `#550` (brand/domain)
- Reuse anchors: `#95` (PWA readiness), `#87` (mobile E2E), `#111` (cloud topology), `#105` (SignalR scale-out), `#216` (GTM execution), `#341` (telemetry)
- Execution order: `v0.1.0` packaging → `v0.2.0` cloud → `v0.3.0` mobile → `v0.4.0` collab → `v0.5.0` maturity → `v1.0.0` GA

### Priority IV (Expansion Tranche: Platform, Test, UX, Docs Maturity)

- Platform and ops maturity: `#84`, `#85`, `#86`, `#101`, `#102`, `#103`, `#104`, `#105`, `#111`
- Test maturity: `#87`, `#88`, `#89` (property/fuzz pilot delivered; extended by `#717`), `#90`, `#91`; rigorous expansion wave tracker at `#721`
- UX and onboarding maturity: `#92`, `#93`, `#94`, `#95`
- Frontend responsiveness maturity: `#213`
- Lower-priority secondary MVP follow-through continuation:
  - `#333` saved views and productivity shortcuts
  - `#334` note-style import and clip intake follow-through
- Expanded-blueprint architecture continuation:
  - `#338` agent mode surfaces and run-detail timeline
  - `#340` integrations registry and supervised connector foundation
- Optional premium UI documentation/component tooling: `#251`
- Developer/user docs maturity: `#99`, `#216`, `#217`
- Deferred capture follow-ons after MVP retention proof: `#218`, `#219`, `#220`
- Outreach CRM deferred expansion wave: `#262` to `#268` (`#263` OUT-01 JSON manifest import delivered)
- Outreach CRM wave reused dependencies: `#75` (delivered import adapters), `#77` (analytics), `#175` (starter-pack catalog expansion)
- MCP production hardening (deferred): `#655` (observability, OAuth, resource subscriptions, key management UI, scope-based permissions)
- Codebase maintainability hotspot refactors (analysis wave): `#158`, `#159`, `#160`, `#161`, `#162`, `#163`, `#164`, `#165`, `#166`, `#167` — ActivityView, BoardView, StarterPackManifestValidator, ArchiveRecoveryService, and AutomationExecutorService decompositions are now delivered; remaining issues in this wave cover other hotspots not yet addressed

### Priority V (Meta/Historical)

- Wave index and historical/closed tracking: `#107` and completed governance items.
- Expanded-blueprint launch-gate/telemetry framing continuation:
  - `#341` product telemetry taxonomy and `R1` / `R2` / `R3` launch-gate follow-through

## Research Reconciliation (WIP PDFs, Feb 2026)

Research sources reviewed:
- `docs/WIP/FutureExpansionAndImprovementsChecklist.pdf`
- `docs/WIP/In-DepthAnalysisAndProgressReport(Feb2026).pdf`
- `docs/WIP/Scaling and Hardening Taskdeck (Vue 3 + ASP.NET Core) - Comprehensive Guide.pdf`

Strategic reconciliation applied:
- Keep current sequence: finish Phase 4 consistency/security first (`Priority I`) before broad feature expansion.
- Translate research recommendations into dependency-aware issues rather than broad unscoped themes.
- Treat non-code operations/configuration work as a mandatory delivery track, not "later ops".
- Added capture/inbox realignment wave from `docs/InReview` planning packs with explicit dependency-mapped issue seeding (`#199` to `#213`).
- Added frontend premium UI foundations wave from `docs/InReview` premium UI pack with deduplicated issue mapping (`#242` to `#251`, reusing `#154`, `#88`, `#92`, `#213`).
- Added testing harness/guardrails wave from `docs/InReview` testing-harness pack with duplicate prevention for already-covered scenarios (`#254` to `#260`).
- Added outreach CRM deferred wave from `docs/InReview/outreach-crm` with low-priority issue seeding (`#262` to `#268`) and explicit reuse of overlapping existing issues (`#75`, `#77`, `#175`).
- Added 2026-03-07 MVP expansion integration from `docs/InReview/MVP_EXPANSION/`; near-horizon now prioritizes novice-first productization and board-centered review workflows before agent/knowledge surface breadth.

## Out-of-Code and Configuration Coverage Matrix

Covered by seeded issues:
- Docker + reverse proxy + compression baseline: `#69` (delivered)
- IaC baseline for single-node AWS environments hosting the Docker workload layer: `#102` (delivered)
  - follow-up hardening now includes SSM-backed JWT secret bootstrap, replace-on-change semantics for host bootstrap drift, a dedicated persistent EBS data volume so SQLite survives routine host replacement, stop-before-detach safety for planned data-volume changes, default destroy-protection for staging/prod data volumes, and backup-bucket noncurrent-version expiry with explicit versioning dependency
- Developer MCP baseline and Docker Marketplace setup hardening: delivered (2026-02-20 local ops cycle)
- MCP operator wiring + verification workflow: `#140` (delivered via `#144`)
- MCP integration smoke/regression harness: `#141` (delivered)
- Staged rollout policy (blue/green/canary): `#101`
- SBOM/release provenance: `#103`
- Cost guardrails: `#104`
- Backup/restore disaster recovery: `#86`
- OpenTelemetry metrics/tracing and alerting runbook: `#68`
- Load/concurrency harness and budgets: `#70` (delivered)
- Multi-tenancy strategy ADR: `#71` (delivered)
- API abuse/rate limiting: `#81` (delivered)
- OWASP/security headers and CSRF/XSS baseline: `#80` (delivered)
- Dependency vulnerability management policy: `#106` (delivered)
  - reusable dependency-security signal workflow now normalizes backend/frontend scan results for PR/manual, nightly, and release contexts; remaining follow-through is limited to future automation escalation (for example auto-ticketing or stricter PR gating) rather than baseline policy definition
- Secrets/configuration management baseline: `#110`
- DB migration strategy and cache strategy: `#84`, `#85`
- Cloud target topology and autoscaling ADR: `#111`
- CI workflow topology expansion/governance baseline: `#168`

Outstanding strategy-level gap to monitor:
- no major out-of-code categories from the reviewed WIP PDFs are currently untracked; residual risk is execution sequencing and closure quality.

## ARCH-01 Follow-Through Stages (Post-ADR)

1. Stage A (Priority II): tenant-context collaboration foundations and isolation semantics alignment (`#72`, `#73`, `#74`, `#75`, `#76` delivered).
2. Stage B (Priority IV): platform data-plane evolution for multi-tenant readiness (`#84`, `#85`).
3. Stage C (Priority IV): tenant-aware DR, rollout, and topology governance (`#86`, `#101`, `#111`).
4. Stage D (Priority III): security/compliance controls that reinforce tenant boundaries (`#80`, `#81` delivered; `#82`, `#83` delivered, `#110` pending).


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
4. Ship first-party packs: common labels + common column flows + 3-5 board blueprints (delivered in PACK-04, issue #50).
5. Reuse package manifests to generate deterministic E2E/QA fixtures (delivered in PACK-05, issue #51).
6. Add pack telemetry to measure adoption, setup-time reduction, and failure points.
7. Add pack migration/version compatibility checks for long-lived boards.
8. Add checklist-ingestion path for chat so pasted plans can map to pack templates and board bootstrap proposals.
## Planning Updates (2026-03-02)

Demo-expansion migration wave seeding completed:
- tracker: `#297`
- dependency-ordered batches: `#298` -> `#299` -> `#300` -> `#301` -> `#302`
- all migration-wave issues carry `Priority I`
- each batch issue now includes a suggested branch name and explicit file-scoped commit expectation

Canonical references for this wave:
- `docs/archive/2026-03-07_docs-root-reorg/DEMO_EXPANSION_MIGRATION_SOT.md`
- `docs/archive/2026-03-07_docs-root-reorg/temp_description.txt`
- `docs/ISSUE_EXECUTION_GUIDE.md`

Batch A baseline delivery (`#298`) status:
- baseline seeding command introduced (`npm run demo:seed`)
- v0-first-run UX defaults applied (advanced surfaces default off, Automations default to Proposals, queue composer instruction-first guidance)
- demo playbook promoted to active docs (`docs/product/DEMO_PLAYBOOK.md`)

Batch B harness/docs delivery (`#299`) status:
- reusable demo harness layer added (`npm run demo:run`, `npm run demo:autopilot`, `scripts/demo-lib.mjs`, `scripts/scenarios/*`)
- scenario modules added for engineering sprint, support triage, and content-calendar demo flows
- API walkthrough asset added: `demo/http/taskdeck-demo.http` (updated for current API contracts)
- stakeholder walkthrough recorder added as opt-in Playwright coverage (`tests/e2e/stakeholder-demo.spec.ts`, gated by `TASKDECK_RUN_DEMO=1`)
- demo operations docs expanded and indexed (`docs/product/DOGFOODING_GUIDE.md`, `docs/USER_MANUAL.md`, `docs/product/DEMO_PLAYBOOK.md`, `docs/INDEX.md`)

Batch C JSON/capture harness (`#300`) status:
- JSON scenario runner added with schema + sample scenarios (`scripts/scenario-json-runner.mjs`, `scripts/scenarios-json/*`)
- `demo:run` now prefers JSON scenarios and supports `--list`, `--skip-llm`, and `--continue-on-error`
- `demo:autopilot` now supports `--loop queue|capture|mixed` and capture controls (`--capture-prob`, `--leave-capture-untriaged-prob`, `--triage-timeout-ms`, `--capture-source`, `--capture-title-hint`)
- capture helper functions added in `scripts/demo-lib.mjs` and consumed by JSON runner/autopilot (`create/get/ignore/cancel/triage/wait-for-outcome`)
- scenario authoring/usage documentation added and indexed (`docs/product/SCENARIOS.md`, `docs/INDEX.md`, `docs/product/DEMO_PLAYBOOK.md`)

Batch D director/artifact orchestration (`#301`) status:
- demo orchestration commands added (`npm run demo:director`, `npm run demo:snapshot`) with new scripts (`scripts/demo-director.mjs`, `scripts/demo-snapshot.mjs`)
- runtime trace stream support added across scenario/autopilot/proposal/capture/ops flows via `TASKDECK_DEMO_TRACE_PATH` (`trace.ndjson` artifact)
- JSON scenario runner expanded with `runOps` step support and `opsRuns` alias namespace
- scenario samples now include Ops template evidence steps (`health.check`) for richer demo artifacts
- stakeholder recorder spec now supports director-mode bootstrap (seed/scenario/autopilot/snapshot orchestration + per-step logs under artifacts)
- playbook and scenario docs updated for director usage and `runOps` authoring guidance

Batch E integration hardening (`#302`) status:
- demo smoke command added (`npm run demo:director:smoke`) for deterministic, LLM-free regression proof with stable artifact output, isolated smoke DB reset, forced fresh Playwright servers, automatic local API port fallback when `5000` is occupied, and actionable remediation hints when explicit runtime port overrides conflict
- default Playwright CI lanes now explicitly pin `TASKDECK_RUN_DEMO=0` so recorder-style demo flows stay opt-in
- `ci-extended.yml` now exposes reusable `demo-director-smoke` workflow wiring for explicit smoke validation (`workflow_dispatch` or PR label `automation`) when the PR touches `.github/workflows/**`, `backend/**`, `frontend/**`, `deploy/**`, or `scripts/**`
- docs/index consolidation completed for demo script entry points, runtime preconditions, and CI policy boundaries
- follow-through hardening now auto-enables live-provider demos for Playwright-backed full walkthroughs when usable demo keys are present, preferring Gemini for long/manual runs while keeping smoke paths deterministic via `--skip-llm`
- non-demo Playwright backend startup now stays pinned to deterministic `Mock` mode by default even when local shell env exports live-provider keys; explicit demo runs still override that baseline when LLM steps are enabled
- post-epic audit hardening under `#310` now also fails fast on unknown scenario IDs, missing starter-pack labels in legacy JS scenarios, and ambiguous duplicate column/label names in JSON scenario resolution
- post-epic audit hardening under `#310` now keeps `demo:seed` rerun-bounded for canonical evidence generation, validates director CLI flags before Playwright passthrough, and keeps recorder board targeting aligned with explicit autopilot-board overrides
- post-epic audit hardening now continues under `#311` so demo runtime/test follow-through stays scoped outside the original migration batches

## Saul-Facing Demo Alignment Wave (2026-03-26)

The new capability spec in `docs/WIP/Taskdeck_Demo_Capability_Specification.md` was reconciled into a narrow delivery wave rather than a broad roadmap reset.

Canonical reconciliation record:
- `docs/analysis/2026-03-26_saul-demo-capability-reconciliation.md`

Execution conclusion:
- the hard substrate is already shipped: capture triage, review-first gating, provenance, board-centered follow-through, and deterministic demo tooling are all present
- the remaining pre-recording gap is business-legible packaging, not missing architecture
- the work should stay pinned to one stakeholder story: `Home -> Inbox/Capture -> Review -> Board`
- execution status now reflects stacked delivery in progress: `#354` plus demo-critical follow-through from `#326` and `#330` are already delivered for this wave, `#355` rehearsal contract is delivered, and `#216` is the remaining pre-recording focus

Seeded issues:
- `#354` `PACK-08`: add a Saul-facing client-onboarding starter pack and deterministic demo scenario
- demo-critical `#326`: trust-first review legibility hardening
- demo-critical `#330`: in-app hero-path/demo-board cues
- `#355` `TST-24`: add the rehearsal contract, acceptance checklist, and artifact expectations for the exact stakeholder path (delivered)
- `#356` `DEMO-00`: track the narrow demo-alignment wave

Reused existing anchors:
- `#326` for proposal readability and trust-cue hardening
- `#330` for in-app demoability and hero-board presentation quality
- `#216` for the broader demo script and public-facing narrative
- `#175` for broader starter-pack expansion after the narrow pre-demo slice

## Manual Product Audit Follow-through Wave (2026-03-26)

The runtime audit in `docs/analysis/2026-03-26_manual-product-audit.md` was reconciled into a focused execution wave rather than left as a read-only artifact.

Canonical reconciliation record:
- `docs/analysis/2026-03-26_manual-product-audit-followthrough.md`

Execution conclusion:
- the golden path is real, but several runtime-coherence gaps still need explicit ownership
- the highest-value follow-through is not broad new feature work; it is truthfulness and trust around realtime health, triage freshness, provider visibility, and docs/runtime alignment
- raw-ID-heavy review readability remains intentionally routed through existing issue `#326` rather than duplicated here

Seeded issues:
- `#363` `ANL-2026-03-26`: tracker
- `#364` `COL-05`: realtime hub CORS/SignalR health
- `#365` `CAP-23`: Inbox triage freshness
- `#366` `UX-20`: Workbench/nav/docs truth alignment
- `#367` `UX-21`: board-history semantic alignment
- `#368` `AUTO-04`: chat live-provider status and first-turn fidelity
- `#369` `TST-25`: headed manual-audit Playwright pack (`Priority IV`)

Immediate hardening landed in this context:
- `GET /api/llm/chat/health` plus explicit Automation Chat provider-state rendering (`live` / `mock` / degraded)
- opt-in live-provider Playwright probe (`tests/e2e/live-llm.spec.ts`)
- headed local audit shortcuts (`npm run test:e2e:audit:headed`, `npm run test:e2e:live-llm:headed`)

## Chat-to-Proposal NLP Gap (2026-03-29)

Manual testing surfaced a significant usability gap in the chat-to-proposal pipeline: natural language requests (e.g., "can you create new onboarding tasks for people who aren't technical?") fail to produce proposals because the pipeline relies on static keyword substring matching (`LlmIntentClassifier`) and regex-based instruction parsing (`AutomationPlannerService.ParseInstructionAsync`). All three LLM providers (Mock, OpenAI, Gemini) share the same brittle classifier; none leverage the LLM for instruction extraction.

Tracker: `#570`. Improvement tiers:
- **Tier 1 (shipped):** classifier hardening with compiled regex, word-distance matching, stemming/plurals, broader verb coverage, and negative context filtering (`#571`); structured parse-hint error responses with closest-match suggestions and frontend hint card with "try this instead" pre-fill (`#572`); substring ordering bug fixed ("remove card" no longer misclassifies as `card.move`)
- **Tier 2 (next):** system prompt + structured output for instruction extraction from real providers (`#573`); multi-instruction parsing for batch requests (`#574`)
- **Tier 3 (delivered):** board-context-aware prompting (`#575`, delivered in `#617`); conversational refinement loop for ambiguous requests (`#576`, delivered in `#791`)
- **Testing (shipped):** dedicated classifier + chat-to-proposal integration tests (`#577`); null guard added to `Classify()`; 86 classifier unit tests + 28 ChatService flow tests

Analysis: `docs/analysis/2026-03-29_chat_nlp_proposal_gap.md`

## Active Blockers (2026-03-29 Manual Test Session)

Two P0 bugs discovered in fresh-registration manual testing must be resolved before Phase 4 can be signed off or any external user onboarding begins. These are data correctness/security failures, not UX polish:

- **`#508`** — Queue list endpoint not scoped to the authenticated user: a fresh-registered account sees all historical queue items from other sessions. Add a `userId` predicate to the LLM queue list query and add a cross-user isolation integration test.
- **`#509`** — Board view auto-switches between boards every few seconds: `boardStore` overwrites `activeBoardId` on each `fetchBoards` response. Add a `preserveSelection` guard so the active board is not reset while it still exists in the refreshed list.

Additional P1 issues from the same session (tracked in `#510`–`#515`) cover excessive board polling, the missing Inbox capture button, chat not emitting proposals, delete-card without confirmation, dark-mode theming gaps on three surfaces, and text-selected cards being non-draggable. Full findings at `docs/analysis/2026-03-29_manual_testing_consolidated_findings.md`.

## Next Best Steps (Immediate)

1. **Resolve `#508` and `#509` (P0 blockers above) before any other backlog work.**
2. Close remaining unblocked Priority I security/policy work first (`#33`, `#34`, `#44`, `#152`) with regression coverage.
2. Run the manual-audit follow-through wave in trust-first order: `#364` -> `#365` -> `#368`, then align product truthfulness through `#366` and `#367`, while routing review-readability detail through `#326`; keep `#369` explicitly lower priority.
3. Run the Saul-facing demo alignment wave as the next narrow product-facing slice: `#354` first, then legibility/demoability follow-through through `#326` and `#330`, then lock the recording contract in `#355` and `#216`.
4. Continue the seeded novice-first shell tranche from `#322`, using the shipped `#320` home/workspace-mode foundation rather than reopening it.
5. Keep the docs/help/testing tranche synchronized with the shipped Wave P core (`#320`, `#322`, `#324`, `#326`, `#96`, `#100`): keep the now-delivered `#328` smoke contract aligned to the shipped first-run loop, and route broader telemetry/release-gate follow-through to `#341`.
6. Keep the delivered testing-harness wave (`#254` to `#260`) in maintenance mode and route any new guardrail expansion through normal follow-up issues while keeping aligned existing seeds `#89`, `#90`, `#106`, and `#168`.
7. Continue managed-key control-plane and abuse follow-through in dependency order: `#235` -> `#237` (quota/kill-switch, not yet started) -> SEC-18 live-traffic wiring follow-up; `#238`/`#239`/`#240` operator tooling and policy groundwork are now delivered.
8. Continue frontend premium UI wave from the delivered foundations: shared primitives (UI-02), PERF-08 budgets, stack decision spike (UI-03), and inbox premium primitives (`#249`/`#788`) are done; next is `#246` (token system audit), `#247` (component reskin pass), and `#248`/`#250` interaction/accessibility hardening.
9. Keep agent substrate and knowledge/integrations work sequenced behind novice-first exit criteria; do not promote them ahead of Horizons A through C.
13. Continue the chat-to-proposal NLP gap (`#570`): Tier 1 delivered — classifier hardening (`#571`), error UX (`#572`), and integration tests (`#577`) are merged; Tier 3 now fully delivered — board-context prompting (`#575`/`#617`) and conversational refinement (`#576`/`#791`) are both merged. Remaining follow-up: enrich audit log entries with changed field details (`#583`).
14. **UX feedback wave (2026-03-31)**: tracker at `#628`; 17 issues seeded from manual testing. Wave 1 delivered 6 fixes (`#612`, `#615`, `#617`, `#621`, `#623`, `#626`). Wave 2 delivered 5 more: both P1 blockers closed — capture triage dash/semicolon delimiters with context hints (`#614`), chat array truncation detection (`#616`); P2 notification type differentiation, grouping, and batch mark-all-read (`#625`); P4 search cursor pagination (`#610`); ops CI-extended path triggers (`#608`). Wave 3 delivered review card sticky footer (`#613`/`#665`). Remaining open from `#628`: 2 P3 strategic spikes (`#618`, `#619`) both completed with implementation waves in progress. Full analysis at `docs/analysis/2026-03-31_manual_testing_ux_feedback.md`.
15. **Hardening and UX wave (2026-04-03)**: 9 issues across 8 PRs (`#691`–`#698`) with adversarial review follow-through: P1 dead workspace routes (`#681`), expired proposal handling in Review (`#678`+`#690`), chat card ID continuity (`#677`), human-readable proposal diffs (`#682`), dark theme label manager (`#684`), chat health banner three-state (`#679`), OpenAI strict mode + loop detection (`#674`), JWT invalidation after account deletion (`#671`/ADR-0021). ~58 new tests added across the wave.
16. **Post-hardening delivery wave (2026-04-03)**: 6 issues across 6 PRs (`#724`–`#731`): SQL-level board metrics filtering (`#675`), double LLM call elimination (`#672`), JWT invalidation hardening with active-user middleware (`#671`), expired proposal review UX with dismiss action (`#678`+`#690`), infrastructure repo integration tests (`#699` — 77 tests, 7 classes, real SQLite, found real ordering bug), LLM write tools + proposal integration (`#650` — 6 write executors, EF migration, 11 total tools, frontend status indicators).
17. **Security + testing + MCP wave (2026-04-04)**: 8 issues across 8 PRs (`#732`–`#739`) with two rounds of adversarial self-review. ~300 new tests added. Key deliveries: SEC-20 ChangePassword identity bypass fix (`#722`/`#732`), golden-path capture→board integration test (`#703`/`#735`), cross-user data isolation tests (`#704`/`#733` — 38 tests, 3 false-positive tests caught in review), worker integration tests (`#700`/`#734` — 24 tests, fake repo status-tracking fixed in review), controller HTTP tests (`#702`/`#738` — 67 tests, 6 controllers, 2 pre-existing bugs found), proposal lifecycle edge cases (`#708`/`#736` — 74 tests, clock-flakiness fixed in review), OAuth/auth edge cases (`#707`/`#737` — 44 tests, found+fixed `ExternalLoginAsync` Substring overflow production bug), MCP full inventory (`#653`/`#739` — 9 resources + 11 tools, user-scoping gap found+fixed in review). Test expansion wave (`#721`) progress: 7 of 22 issues now delivered (`#699`, `#700`, `#702`, `#703`, `#704`, `#707`, `#708`); remaining 15 open.
18. **Tech-debt, security, and feature hardening wave (2026-04-04)**: 7 issues across 7 PRs (`#765`–`#770`, `#776`) with two rounds of adversarial review per PR (~65 new tests: 32 backend + 33 frontend). Key deliveries: Agent API 500 fix (`#758`/`#776` — `DateTimeOffset` ORDER BY in SQLite, `AgentRunRepository` upgraded to `IsSqlite()` SQL-level pattern, round 2 caught load-all-before-limit perf bug), DataExport exception logging (`#759`/`#766` — `ILogger` added to `DataExportService`/`AccountDeletionService`, round 2 added `OperationCanceledException` filter + `CancellationToken.None` rollback), streaming chat token usage (`#763`/`#768` — `LlmTokenEvent` extended, all 3 providers populated, `StreamResponseAsync` now persists messages + records quota), EF Core version alignment (`#760`/`#767` — 9.0.14→8.0.14, EF9-only API removed, `FrameworkReference` swap, round 2 added `PrivateAssets`), frontend HTTP interceptor/auth guard tests (`#725`/`#765` — 33 tests, round 2 fixed ESLint `no-import-assign` CI breaker), OAuth token lifecycle tests (`#723`/`#769` — 19 tests covering auth code store + JWT lifecycle + SignalR auth, round 2 fixed `HttpClient` leak + misleading test names), tool argument replay (`#673`/`#770` — `Arguments` field on `ToolCallResult`, OpenAI/Gemini replay now uses real arguments). Test expansion wave (`#721`) progress: 23 of 25 issues now delivered (waves 4+5 added `#711`, `#712`, `#716`, `#720`, `#723`, `#725`); remaining 2 open (`#705`, `#717`).
19. **Feature, analytics, MCP, chat, testing, and UX expansion wave (2026-04-08)**: 7 issues across 7 PRs (`#787`–`#793`) with two rounds of adversarial review per PR (~390+ new tests). Key deliveries: exportable analytics CSV (`#78`/`#787` — `MetricsExportService` with CSV injection protection, `ADR-0022` deferring PDF, 29 tests, adversarial review caught embedded-newline injection HIGH), forecasting service (`#79`/`#790` — heuristic `ForecastingService` with rolling-average throughput, std-dev confidence bands, frontend MetricsView section, 32 tests, adversarial review caught throughput double-counting HIGH + history window bug), MCP HTTP transport + API key auth (`#654`/`#792` — `ApiKey` entity with SHA-256, `ApiKeyMiddleware`, `HttpUserContextProvider`, `MapMcp()`, REST key management, rate limiting, 31 tests, adversarial review caught key-existence oracle + modulo bias), conversational refinement loop (`#576`/`#791` — `ClarificationDetector` with strong/weak signal split, max 2 rounds + skip, Mock simulation, frontend badge + skip button, 41 tests, adversarial review caught false-positive heuristic HIGH), concurrency stress tests (`#705`/`#793` — 13 `SemaphoreSlim`-barrier stress tests for queue claims, card conflicts, proposal races, rate limiting, multi-user), property-based adversarial tests (`#717`/`#789` — 211 FsCheck + fast-check tests across domain/API/frontend, no 500s from any input), inbox premium primitives (`#249`/`#788` — `TdSkeleton`/`TdInlineAlert`/`TdEmptyState`/`TdBadge` rework, 7 tests). Test expansion wave (`#721`) progress: 25 of 25 issues now delivered (this wave closed `#705` and `#717`). Additional issues closed: `#78`, `#79`, `#249`, `#576`, `#654`.
10. Keep issue `#107` synchronized as the single wave index and maintain one-priority-label-per-issue discipline (`Priority I` to `Priority V`).
11. Treat the demo-expansion migration wave (`#297` -> `#302`) as delivered; route any further demo-tooling work through normal scoped follow-up issues such as `#311`, `#354`, `#355`, and `#369` instead of reopening the migration batches.
12. Test suite baseline counts recertified 2026-04-08: backend ~3,460+ passing, frontend ~1,891 passing, combined ~5,370+. Rigorous test expansion wave (`#721`) fully delivered (25/25 issues).
20. **Platform expansion wave (2026-04-09)**: 10 issues (`#84`, `#85`, `#87`, `#88`, `#90`, `#91`, `#95`, `#104`, `#105`, `#111`) across 10 PRs (`#796`–`#805`) delivered platform hardening (PLAT-01/02/03), testing infrastructure (TST-02/03/05/06), PWA readiness (UX-09), and ops documentation (OPS-12/14). 5 new ADRs (ADR-0023 through ADR-0027). Two rounds of adversarial review per PR caught 22 CRITICAL + 32 HIGH issues, all resolved. New test projects: `Taskdeck.Integration.Tests` (Testcontainers). New CI workflows: cross-browser matrix, visual regression, mutation testing, container integration. New infra: `ICacheService`, SignalR Redis backplane, VitePWA service worker.

## Documentation Operating Model
Active docs:
- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/TESTING_GUIDE.md`
- `docs/MANUAL_TEST_CHECKLIST.md`

Audience-first product docs:
- `docs/START_HERE.md`
- `docs/USER_MANUAL.md`
- `docs/product/DEMO_PLAYBOOK.md`

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
- Risk: capture pipeline breaks on natural-language input, undermining near-zero-friction thesis
  - Mitigation: phased improvement — regex delimiter expansion first, LLM-assisted extraction second, semantic pipeline long-term (`#614`)
- Risk: LLM tool-calling / MCP architecture becomes scope-creep or breaks review-first safety
  - Mitigation: spike-first approach (`#618`, `#619`); write tools MUST produce proposals, never direct mutations; read tools are ungated
