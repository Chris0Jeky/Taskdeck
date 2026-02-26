# Taskdeck Implementation Masterplan

Last Updated: 2026-02-25  
Planning Horizon: Next 8 to 12 weeks  
Companion Active Docs:
- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/TESTING_GUIDE.md`
- `docs/MANUAL_TEST_CHECKLIST.md`
- `docs/GOLDEN_PRINCIPLES.md`

## Purpose

This is the active execution guide for sequencing implementation.
Update this file at the end of each meaningful delivery cycle.

## Planning Principles

- `docs/STATUS.md` is authoritative for current shipped reality.
- Product north star: make capture nearly free and keep automation safe through review-first proposals.
- Prefer finishing cross-cutting consistency work before adding new surface area.
- Security and identity convergence remains the highest-priority engineering track.
- Cross-user existence policy is fixed: return `403` for authenticated-but-unauthorized access and `404` for true missing resources.
- Automation remains proposal-first and review-first by default.
- Do not claim or ship silent/destructive autonomy by default; trust posture takes precedence over convenience.
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
   - Backend: 887 passing
   - Frontend unit: 378 passing
   - E2E: 23 passing
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
28. PACK-04 first-party starter packs v1 delivery:
   - added API-backed first-party starter-pack catalog endpoint: `GET /api/boards/{boardId}/starter-packs/catalog`
   - shipped first-party pack coverage for common labels, common column flow, and 3 board blueprints
   - added backend/frontend tests for catalog availability, pack-category coverage, and manifest validity
29. PACK-05 deterministic fixture packs delivery:
   - added Playwright starter-pack fixture bootstrap helper flow for manifest-backed deterministic board-state setup
   - shipped deterministic fixture manifests for `small`, `medium`, and `edge` scenarios
   - added dedicated E2E coverage for fixture bootstrap success and conflict dry-run paths
30. DEBT-01 nullability reduction delivery:
   - eliminated current domain `CS8618` warnings by applying EF-safe non-null default initialization patterns
   - validated no behavior regressions via full backend solution test pass
31. DEBT-02 log-query scalability pass delivery:
   - replaced broad in-memory log composition with repository-filtered query paths
   - removed command-run log query N+1 pattern by introducing direct filtered log querying with run correlation/user projection
   - validated logs API contract behavior and full backend regression suite pass
32. DEBT-03 database export/import delivery:
   - added authenticated database export/import API routes (`GET /api/export/database`, `POST /api/import/database`)
   - implemented minimal-safe SQLite file export/import with Development-sandbox gating, payload signature/size validation, and backup-restore fallback on file replacement failure
   - added application and API integration coverage for auth, sandbox gating, and import validation paths
33. COL-01 realtime board updates delivery:
   - added SignalR `BoardsHub` with claims-derived board subscription authz checks and board-scoped group subscriptions
   - added application-layer board mutation notifications for board/card/column/label writes and wired hub fan-out notifier in API composition root
   - integrated frontend board realtime lifecycle (join/switch/leave/reconnect) with websocket-unavailable polling fallback and expanded API/unit/E2E regression coverage
34. OBS-01 observability baseline delivery:
   - added OpenTelemetry startup wiring for ASP.NET + HttpClient instrumentation with Taskdeck custom activity source and meter registration
   - added worker/queue/heartbeat telemetry emission with stable metric names and dimension keys
   - added correlation ID propagation into trace tags plus a versioned observability baseline runbook with dashboard/alert/smoke-verification guidance
35. OPS-07 containerized deployment baseline delivery:
   - added production-oriented backend/frontend Dockerfiles and compose profile with reverse-proxy entrypoint
   - added proxy compression + forwarded-header/security-header posture and staging/local deployment runbook
   - added CI container image build/export artifacts with reproducible compose render checksums
36. Developer MCP tooling posture expansion:
   - enabled a broader Docker Marketplace MCP server bundle (SQLite, JetBrains, Postman candidate, OpenAPI, filesystem, terraform, time, etc.)
   - stabilized default Docker gateway server set for Codex project config to avoid secret-gated startup failures while preserving optional integrations
   - documented setup/credential expectations in `docs/MCP_TOOLING_GUIDE.md`
37. MCP operations workflow integration:
   - added operator runbook (`docs/MCP_OPERATIONS_RUNBOOK.md`) covering credential setup, validation, troubleshooting, and recurring checklists
   - added helper scripts to wire credential-gated Docker MCP servers and verify baseline/optional MCP dry-run paths
   - integrated MCP operations checks into active testing guidance
38. TST-07 MCP smoke/regression harness delivery:
   - enhanced MCP profile validation script with optional-server prerequisite diagnostics (missing secret/config classification)
   - codified strict/warning/skip behavior for optional integrations and documented CI-friendly command patterns
   - added deterministic CI status output contract (`PASS`, `PASS_WITH_WARNINGS`, `FAIL`) for MCP profile validation flows
39. OPS-19 CI topology first-pass delivery:
   - migrated required CI entrypoint from `.github/workflows/ci.yml` to `.github/workflows/ci-required.yml` with equivalent gate behavior
   - extracted docs governance lane into reusable workflow `.github/workflows/reusable-docs-governance.yml` as baseline for incremental workflow decomposition
40. OPS-19 CI topology second-pass delivery:
   - extracted backend architecture lane into reusable workflow `.github/workflows/reusable-backend-architecture.yml` and routed `ci-required.yml` through it
   - extracted frontend unit lane into reusable workflow `.github/workflows/reusable-frontend-unit.yml` (preserving Ubuntu/Windows matrix behavior) and routed `ci-required.yml` through it
41. OPS-19 CI topology API-integration extraction delivery:
   - extracted API integration lane into reusable workflow `.github/workflows/reusable-api-integration.yml` and routed `ci-required.yml` through it (preserving Ubuntu/Windows matrix behavior)
42. OPS-19 CI topology third-pass delivery:
   - added `merge_group` trigger parity to `.github/workflows/ci-required.yml` to align merge-queue required-check execution with PR/push paths
43. OPS-19 CI topology fourth-pass delivery:
   - extracted backend unit lane into reusable workflow `.github/workflows/reusable-backend-unit.yml` (preserving Ubuntu/Windows matrix behavior and domain/application/CLI split coverage)
   - routed `.github/workflows/ci-required.yml` through the reusable backend unit lane
44. OPS-19 CI topology fifth-pass delivery:
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
50. SEC-11 LLM queue board-scope authorization follow-through (`#152`):
   - `POST /api/llm-queue` now enforces board-read authorization when `boardId` is supplied
   - queue creation now aligns to policy (`403` for authenticated cross-user unauthorized board access, `404` for true missing boards)
   - regression coverage expanded in `LlmQueueServiceTests`, `LlmQueueApiTests`, and `AuthzRegressionMatrixApiTests`
51. SEC-11 API regression coverage final sweep (`#152`):
   - expanded cross-user `403` coverage for board update and board-access management (`list/grant/update/revoke`)
   - expanded chat authorization coverage for cross-user forbidden access and true-missing session `404` branches (`get session`, `send message`)
   - API integration suite increased to 185 passing tests with explicit `403/404` branch locking for remaining protected route gaps
52. API-06 centralized exception/fallback error-contract hardening (`#153`):
   - added global unhandled-exception middleware in the API pipeline to return deterministic `ApiErrorResponse` payloads for unexpected server failures
   - standardized unknown-result fallback `500` mapping to `ApiErrorResponse` (`UnexpectedError`) instead of `ProblemDetails` to keep fallback payload shape contract-uniform
   - added fault-injection API integration coverage validating unhandled-failure contract shape, non-leakage message behavior, and correlation-header continuity under `500` responses
53. TST-14 architecture-guard expansion (`#157`):
   - expanded architecture tests beyond csproj references with source-layer purity invariants for Domain/Application forbidden namespace imports
   - added API controller boundary invariants to restrict direct `ControllerBase` inheritance to auth/health controllers and enforce `[Authorize]` declaration on protected controllers
   - architecture guard suite now emits deterministic file-scoped diagnostics for quick remediation in CI and local runs
54. TST-01 load/concurrency harness delivery (`#70`):
   - added k6 board-heavy API regression profile (`tests/load/k6/board-heavy-load.js`) with seeded-auth setup, read/write traffic mix, thresholds, and failure diagnostics
   - added multi-session Playwright concurrency harness coverage (`frontend/taskdeck-web/tests/e2e/concurrency.spec.ts`) for conflicting edits and realtime cross-session propagation
   - added reusable CI lane (`.github/workflows/reusable-load-concurrency-harness.yml`) and wired it into `ci-extended` (testing label/manual) plus `ci-nightly` with persisted k6/Playwright artifacts
55. ARCH-01 multi-tenancy strategy ADR delivery (`#71`):
   - added accepted ADR at `docs/analysis/2026-02-22_multi-tenancy-strategy-adr.md` comparing `database-per-tenant`, `schema-per-tenant`, and `shared-schema + TenantId`
   - selected `shared-schema + TenantId` as immediate rollout model with explicit promotion path to `database-per-tenant` for high-isolation tiers
   - defined phased migration/enforcement plan plus tenant-isolation readiness checklist and cross-tenant `403` test strategy expectations
56. FE-11 frontend lint baseline + CI enforcement (`#154`):
   - added pragmatic Vue 3 + TypeScript ESLint baseline (`.eslintrc.cjs`) with focused rule suppressions to avoid style-churn while catching correctness issues
   - added `npm run lint` script with zero-warning enforcement and integrated lint into reusable frontend CI lane (`reusable-frontend-unit.yml`)
   - documented frontend lint execution and suppression guidance in active testing docs to keep lint policy explicit for contributors
57. FE-12 frontend coverage threshold gate (`#155`):
   - codified global and critical-surface Vitest coverage thresholds (`src/api`, `src/store`, `src/composables`, `src/utils`, `src/components/board`) in frontend test configuration
   - switched reusable frontend CI lane to threshold-enforced coverage execution and standardized machine-readable triage artifacts (JUnit + coverage JSON/HTML)
   - documented explicit ratchet policy (thresholds can remain or increase, never decrease) and local threshold-breach verification command
58. COL-02 notifications framework delivery (`#72`):
   - added notification persistence model (`Notifications`, `NotificationPreferences`) with user-scoped preference toggles for event-family cadence controls and in-app channel enablement
   - shipped authenticated notification APIs (`GET /api/notifications`, `POST /api/notifications/{id}/read`, `GET/PUT /api/notifications/preferences`) with board-filter authorization guardrails and deduplication-aware publish semantics
   - integrated frontend notification inbox/preferences routes + Pinia store/api clients and added regression coverage for backend event publication, API auth/filter behavior, and frontend inbox/preferences interactions
59. COL-03 collaborative presence/conflict policy delivery (`#73`):
   - added SignalR-backed board/card presence snapshots with active viewer/editor state publication on join/leave/disconnect and card editing focus changes
   - added optimistic card update conflict policy via `ExpectedUpdatedAt` with deterministic `409 Conflict` user feedback and stale-write conflict audit logging (actor + expected/actual timestamps)
   - expanded backend/frontend regression coverage, including multi-session Playwright conflict scenario validation and realtime presence broadcast assertions
60. COL-04 threaded card comments and mentions workflow delivery (`#74`):
   - added authenticated board/card comment APIs for create/list/reply/update/delete with reply-depth guardrails and moderation constraints (author or board owner/admin)
   - added mention parsing + actor-linking for card comment bodies with board-read permission checks before mention notification publication
   - added card-comment audit entries and frontend card-modal comment UI flow (thread list, reply, edit, delete), with backend/frontend test coverage for mention parsing and authorization boundaries
61. Capture realignment backlog seeding delivery (`#199` to `#213`):
   - reconciled in-review capture/security/performance planning packs into dependency-mapped GitHub issues
   - seeded a dedicated capture wave tracker (`#199`) with execution issues (`#200` to `#211`) plus linked security/performance follow-through (`#212`, `#213`)
   - updated existing SEC-06 rate-limiting issue (`#81`) and wave index (`#107`) to integrate capture-specific scope without duplicate issue creation
62. InReview extraction coverage expansion (`#216` to `#220`):
   - seeded go-to-market and user-research execution issues from HUMAN playbooks (`#216`, `#217`)
   - seeded deferred capture follow-ons from the original realignment pack (`#218`, `#219`, `#220`)
   - updated capture wave tracker (`#199`) and wave index (`#107`) to keep extraction coverage explicit
63. CAP-01 capture model/domain contract delivery (`#200`):
   - accepted queue-wrapper MVP model (`LlmRequest` + `inbox.capture.v1`) with explicit migration path to dedicated capture entities
   - added canonical capture source/status contracts plus transition policy mapping from queue lifecycle states
   - added capture payload schema/invariant enforcement (schema version, raw text bounds, actor-field rejection) and provenance linkage representation for capture item -> triage run -> proposal
64. CAP-03 queue provenance fix delivery (`#202`):
   - extended planner contract to support explicit source metadata (`sourceType`, `sourceReferenceId`, `correlationId`) with manual-safe defaults
   - queue worker now stamps queue-origin proposals as `ProposalSourceType.Queue` instead of `Manual`
   - queue item id is now forwarded as source-reference and correlation metadata for deterministic provenance traceability
65. CAP-02 capture API slice delivery (`#201`):
   - added authenticated `/api/capture/items` API surface for create/list/detail/ignore/cancel actions with claims-derived user scoping
   - create endpoint now returns `201 Created` and persists capture payloads via queue-wrapper model (`LlmRequest` + `inbox.capture.v1`)
   - list/detail contracts now enforce excerpt-only list payloads and detail-only full text visibility, with idempotent ignore/cancel action behavior and cross-user `403` vs true-missing `404` policy coverage
66. CAP-04 triage enqueue + state transition delivery (`#203`):
   - added authenticated triage enqueue endpoint: `POST /api/capture/items/{id}/triage` returning `202 Accepted`
   - capture triage enqueue now returns deterministic triage state (`Triaging`) with explicit idempotent replay signaling (`AlreadyTriaging`)
   - invalid-state transitions now return stable `Conflict` error-contract payloads, including ignored/cancelled capture items
   - queue processing guardrails now skip pending capture request types (`inbox.capture.v1`) to preserve explicit triage-trigger semantics ahead of CAP-05 worker routing
67. CAP-05 triage worker routing and proposal generation delivery (`#204`):
   - queue worker now routes triaging capture items (`inbox.capture.*` + `Processing`) through a dedicated capture-triage pipeline rather than generic planner parsing
   - deterministic extraction baseline now converts checklist/bullet/numbered capture content into proposal operations with stable idempotency keys
   - triage pipeline now persists provenance linkage (`capture item -> triage run -> proposal`) on capture payloads and exposes `ProposalCreated` capture status once linked
   - capture triage failure paths now return deterministic non-mutating outcomes (no direct board writes), with bounded retry behavior retained under worker policy
68. CAP-06 strict triage contract + prompt versioning delivery (`#205`):
   - added strict triage output contract (`capture-triage-output.v1`) with version + prompt invariants and explicit machine-readable schema file under `Taskdeck.Application/Schemas`
   - triage proposal generation now validates structured output against schema constraints before creating proposals, with deterministic `ValidationError` outcomes on contract violations
   - triage provenance persistence now includes `promptVersion` (`triage.v1`) for each successful triage run (`capture item -> triage run -> proposal`)
   - added deterministic fixture-backed validation coverage (golden + negative cases for missing tasks, wrong prompt version, unknown properties)
69. CAP-07 inbox frontend route/list/detail delivery (`#206`):
   - added workspace inbox surface (`/workspace/inbox`) with shell navigation and router integration
   - inbox list now renders excerpt-first capture summaries, while full raw capture text is fetched only on detail open
   - inbox detail now supports deterministic ignore/cancel actions with refreshed capture state after mutation calls
   - keyboard-first inbox navigation (`ArrowUp`/`ArrowDown`/`Enter`) plus escape-stack compliant detail close behavior is now covered by frontend regression tests
70. CAP-08 capture modal + command palette/hotkey delivery (`#207`):
   - added quick capture modal with keyboard-first submit (`Ctrl+Enter`) and deterministic close behavior
   - command palette now includes explicit capture action command while preserving inbox navigation command access
   - global quick capture hotkey (`Ctrl+Shift+C`) now opens capture modal from workspace shell contexts
   - successful capture submission now routes directly to inbox and surfaces the new item in list state for immediate follow-through
71. CAP-09 inbox triage trigger + proposal-linking UX delivery (`#208`):
   - inbox detail now includes explicit triage enqueue action with deterministic in-progress/completion state handling
   - capture detail contract now surfaces provenance linkage metadata (`capture item -> triage run -> proposal`) for UI consumers
   - inbox detail now renders direct proposal review navigation when triage yields a linked proposal id
   - frontend regression suite now covers triage action success/failure and proposal-link rendering paths
72. CAP-10 card/proposal provenance UX delivery (`#209`):
   - added card provenance API contract for capture-created cards (`GET /api/boards/{boardId}/cards/{cardId}/provenance`) with board-scope authz guardrails (`403` cross-user)
   - capture triage create-card operations now persist deterministic card target ids so provenance lookup remains stable after proposal execution
   - card modal now surfaces capture-origin marker, capture/proposal deep-links, proposal status, and triage-run metadata when provenance exists
   - automations proposal surface now exposes capture-linked context (capture artifact link + triage-run reference), with frontend/backend regression coverage
73. CAP-11 capture loop end-to-end regression delivery (`#210`):
   - added dedicated Playwright regression (`tests/e2e/capture-loop.spec.ts`) for capture create -> triage -> proposal approve/execute -> card provenance verification
   - end-to-end flow now validates proposal-first trust posture by asserting board mutation only after explicit proposal approval and execute action
   - regression asserts resulting card provenance links (`Open Capture`, `Open Proposal`) and triage-run metadata visibility in card modal
   - full Playwright suite now exercises capture-loop path by default to guard against cross-surface regressions
74. CAP-12 canonical docs promotion delivery (`#211`):
   - updated canonical docs (`docs/STATUS.md`, `docs/IMPLEMENTATION_MASTERPLAN.md`, `docs/TESTING_GUIDE.md`, `docs/MANUAL_TEST_CHECKLIST.md`) to represent capture MVP as shipped behavior
   - moved capture validation language from planned-only posture to active regression posture in testing and manual guides
   - marked original in-review capture pack READMEs as historical/stale after canonical promotion
75. TST-17 drag/drop persistence regression coverage delivery (`#256`):
   - `tests/e2e/smoke.spec.ts` now asserts card drag/move persistence after a full page reload by validating target-column presence and source-column absence post-refresh
   - `tests/e2e/smoke.spec.ts` now asserts column reorder persistence after a full page reload using explicit ordered heading checks
   - drag-handle safety coverage in smoke was hardened to use stable add-card control coordinates for non-handle drag attempts, reducing intermittent setup flake while preserving behavior assertions
76. AUTO-03 provider-agnostic runtime delivery (`#232`):
   - expanded runtime provider support to `OpenAI` + `Gemini` behind deterministic environment/config gates with explicit `Mock` fallback on invalid live-provider configuration
   - added Gemini provider adapter (`generateContent`) and parity fallback behavior across success/failure/invalid-response/cancellation branches
   - capture triage provenance now persists provider/model metadata (`provider`, `model`) alongside `promptVersion` for linked triage/proposal flows
   - expanded regression coverage across selection policy, provider adapters, capture provenance surfaces, and API chat integration with non-mock provider stubs
   - follow-on managed-key identity attribution baseline (`#236`) now threads server-derived attribution (`userId`, correlation ID, source surface, board/session scope) through chat/provider boundaries, persists attribution in capture provenance, and adds spoofing/propagation regression coverage
77. INT-01 external import adapters foundation delivery (`#75`):
   - added provider-registry external import orchestration (`IExternalImportAdapter`, `IExternalImportService`) so new providers can be added without core import-service rewrite
   - shipped CSV adapter baseline with outreach-contact profile mapping and deterministic dedupe key ordering (`linkedin_url` -> `email` -> normalized `display_name+company`)
   - added board-scoped authenticated import endpoint (`POST /api/boards/{boardId}/imports/external`) with dry-run/apply result contracts (`create/update/skip/conflicts`) and rollback-safe apply behavior
   - added backend regression coverage for malformed CSV, duplicate input handling, deterministic upsert behavior, rollback safety, archived-board rejection behavior, and CSV payload/row guardrails, plus operator-facing mapping guidance in `docs/IMPORT_ADAPTERS_GUIDE.md`
78. INT-02 webhook integration security model delivery (`#76`):
   - added board-scoped outbound webhook subscription and delivery contracts (`POST/GET/PATCH/DELETE /api/boards/{boardId}/webhooks`) with authz-safe ownership and revocation handling
   - added mutation-event queueing and signed webhook dispatch (`X-Taskdeck-Webhook-*` headers) with HTTPS/default host safety checks and localhost gating controls
   - added worker/runtime hardening for atomic claim/reload flow, non-success response retry scheduling, dead-letter terminal handling, and stale-processing recovery
   - added backend regression coverage across domain/application/API/worker/repository webhook paths, including non-success dispatch retry/dead-letter branches
79. API CORS development-origin configurability delivery:
   - API CORS composition now keeps default localhost origins (`http://localhost:5173`, `http://localhost:5174`) as baseline behavior
   - development fallback localhost origins (`http://localhost:4173`, `http://localhost:5001`) are now included so restricted local frontend-port runs remain preflight-safe
   - development runtime now accepts additive allowed origins from configuration key `Cors:DevelopmentAllowedOrigins`
   - API integration coverage now verifies both default-origin allowance and development-configured alternate-origin allowance via deterministic in-memory config overrides
80. OPS-16 deployment/container hardening verification matrix delivery (`#142`):
   - added deployment verification script (`scripts/deploy/Verify-TaskdeckDeploymentHardening.ps1`) covering secret-enforcement validation, reverse-proxy header checks, unauthorized-path checks, and startup/restart/shutdown reliability checks for the compose baseline
   - added explicit pass/fail matrix doc (`docs/DEPLOYMENT_HARDENING_MATRIX.md`) and linked it from deployment/testing docs for deterministic operator execution
   - expanded manual checklist coverage for non-automatable deployment controls (backend exposure posture, edge TLS termination posture, host restart rehearsal expectations)
81. PACK-07 warning-first starter-pack apply UX delivery (`#176`):
   - starter-pack apply conflict contract now includes severity (`blocking`/`warning`) and controller conflict responses now hard-stop only on blocking conflicts
   - starter-pack apply service now marks non-blocking seed-card skip paths as warnings and preserves apply success when only warnings exist
   - starter-pack modal now shows explicit applied/skipped/blocked/warnings outcome summaries with warning-first messaging, and backend/frontend regression coverage now locks warning-vs-blocking behavior
82. TST-18 Playwright frontend port-resolution hardening delivery:
   - frontend E2E config now resolves fallback ports deterministically across Playwright runner and worker imports
   - local runs (server reuse enabled) prefer identity-verified running Taskdeck frontend listeners before bind probes to prevent runner/worker drift (`4173` to `5001`)
   - CI runs (server reuse disabled) prefer bindable ports first so stale listeners do not trigger `url is already used` startup failures
   - fallback port selection now persists first resolution in-process (`TASKDECK_E2E_RESOLVED_FRONTEND_PORT`) so worker config imports do not diverge from runner webServer startup port
   - local Windows E2E gate now re-verifies with `npx playwright test --reporter=line` using fallback path (`5173` -> `4173` -> `5001`)
83. FE-13 local dev server startup hardening delivery:
   - `npm run dev` now launches through a small Vite wrapper that auto-resolves restricted/unavailable local ports with fallback order `5173` -> `4173` -> `5001`
   - wrapper now selects the first bindable candidate port and skips occupied candidates for new Vite processes, preventing strict-port startup failures on stale listeners
   - wrapper now sets strict-port startup semantics by default, avoiding implicit Vite auto-increment drift when a requested port is occupied
   - explicit local overrides remain supported (`--host`, `--port`, `TASKDECK_DEV_PORT`) for reproducible manual debugging
   - manual local flows no longer require one-off fallback command rewrites when `localhost:5173` is blocked with `listen EACCES`
84. OPS-19 container-image frontend dependency-policy unblock follow-through:
   - frontend npm dependency graph now keeps `@microsoft/signalr` on its supported `ws@7.5.10` major line via a vendored local tarball dependency (`ws: file:vendor/ws-7.5.10.tgz`) so container `npm ci` no longer fetches blocked registry tarballs for that version
   - frontend npm dependency graph now uses `p-limit@3.0.2` override (compatible with `p-locate@5`) to remove blocked `yocto-queue-0.1.0` fetches without cross-major override drift
   - refreshed lockfile keeps container `npm ci` deterministic and unblocks `.github/workflows/reusable-container-images.yml` frontend build stage
   - local Docker validation confirms `deploy/docker/frontend.Dockerfile` build-stage `npm ci` and `npm run build` both complete successfully with the override
85. UX-11 archive lifecycle control refinement (`#177`):
   - board settings lifecycle controls now use one explicit archive/restore action with deterministic confirmation messaging, replacing duplicate archive semantics in the same surface
   - archive workspace now supports hiding archived boards from the default list, explicit hidden-board reveal (`Show Hidden Boards`), and reversible unhide actions for clearer long-tail archive management
   - archive/frontend regression coverage now locks hidden-board visibility filtering behavior while API integration coverage locks archive/restore lifecycle transitions via board update contracts

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
- maintain shipped provider-agnostic live-provider runtime (`OpenAI` + `Gemini`) and demo-first setup posture (`#232`, delivered)
- maintain delivered managed-key identity attribution baseline (`#236`) and complete remaining control-plane foundations for shared provider-token exposure (`#235`, `#237`)
- expand planner operation extraction in a structured, test-backed way
- harden executor behavior for partial failure semantics and audit quality
- improve archive and automation coherence for board-level restore/execution workflows

Exit Criteria:
- provider strategy supports safe mock/prod switching
- provider selection/runtime behavior is provider-agnostic at app-service boundaries (`ILlmProvider`) and test-backed for `OpenAI` + `Gemini` + `Mock` fallback
- managed-key mode has explicit attribution and quota control baselines before broad external exposure
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
- integrations/webhooks/connectors foundation (initial webhook integration security model delivered in `#76`)
- analytics and planning surfaces
- compliance/security expansion (SSO/MFA, data portability, dependency-security policy)
- UX maturity (accessibility, search, onboarding, offline readiness)

Exit Criteria:
- collaboration and integration foundations are production-safe and test-backed
- growth-oriented UX and analytics features remain consistent with security and operability controls

### Horizon F (Realignment Track): Capture Inbox MVP and Trust Guardrails

Focus:
- inbox-first capture flow for low-friction thought/task ingestion
- deterministic triage pipeline that feeds proposal-first automation (never direct auto-apply)
- provenance visibility from capture artifact to proposal and resulting cards
- capture-specific security/performance hardening (rate limiting, logging redaction, long-list responsiveness)

Exit Criteria:
- capture create/list/detail/triage loop is shipped with policy-correct API behavior
- proposal linkage/provenance is visible in UI and audit-safe
- end-to-end capture -> triage -> review -> apply regression is stable
- canonical docs and manual verification steps reflect the new workflow

Non-goals for this horizon:
- no full-autonomy agent mode for destructive operations
- no requirement to ship voice/transcription sources before typed/paste capture loop retention is proven
- no bypass of Priority I security/policy ordering rules

### Horizon G (Frontend Premium UI Foundations Track)

Focus:
- establish frontend design-system foundations (semantic tokens, theme/density/motion contracts)
- build a shared primitives layer for shell/board/inbox surfaces
- execute focused premium reskin passes for AppShell, Board, and Inbox without behavior regression
- harden board interaction quality (drag/drop responsiveness + keyboard alternatives)
- integrate premium UX performance instrumentation and keep existing quality gates explicit

Execution tracker and seeded issues:
- `#242` UI-00 tracker
- `#243`, `#244`, `#245`, `#246`, `#247`, `#248`, `#249`, `#250`
- optional Priority IV add-on: `#251`
- reused dependencies (no duplicate wave issue): `#154`, `#88`, `#92`, `#213`

Exit Criteria:
- token + primitive foundations are in place and reused by multiple major surfaces
- AppShell/Board/Inbox premium passes are shipped with regression coverage
- accessibility/keyboard and reduced-motion contracts are explicit and test-backed
- visual/performance gates are reconciled with ongoing frontend CI/test operations

### Horizon H (Testing Harness and Guardrails Track)

Focus:
- eliminate deterministic flake vectors in automated suites (`Thread.Sleep`, ad-hoc polling loops)
- strengthen high-signal regression coverage for drag/drop persistence and API error-contract completeness
- add harness-level contract guardrails (OpenAPI generation + parse validation artifacts)
- codify golden principles and enforce a minimal mechanical subset
- add non-blocking nightly quality telemetry (coverage artifacts + dependency/security signal collection)

Execution tracker and seeded issues:
- `#254` TST-15 tracker
- `#255`, `#256`, `#257`, `#258`, `#259`, `#260`
- explicit reuse (no duplicate wave issue): existing WIP limit, sandbox gate, and starter-pack idempotency coverage already implemented

Recent progress (2026-02-24):
- `#260` adds a dedicated non-blocking nightly-quality workflow (`.github/workflows/nightly-quality.yml`) for scheduled/manual coverage and dependency/security signal collection
- nightly-quality artifacts now provide reporting-first telemetry (coverage outputs + vulnerability/audit command results) without changing required PR gate behavior
- `#259` adds `docs/GOLDEN_PRINCIPLES.md` and a lightweight mechanical enforcement script (`scripts/check-golden-principles.mjs`) in the reusable docs-governance lane
- docs-governance now also requires/index-checks `docs/GOLDEN_PRINCIPLES.md` and validates a date-stamp line to keep principle governance deterministic
- `#258` adds a reusable OpenAPI generation/parse-validation lane with artifact upload wired into `ci-extended` and `ci-nightly`
- OpenAPI drift snapshot/diff hard-gating is intentionally deferred; current lane enforces generation + parse-contract validity and publishes inspection artifacts
- `#257` now centralizes representative API error-contract assertions for `400/401/403/404/409` in `ApiErrorContractApiTests`
- representative error-path coverage now includes `X-Request-Id` response echo assertions where middleware applies

Exit Criteria:
- deterministic flake vectors identified in the wave are removed from current suites
- drag/drop persistence and representative error-contract classes are regression-locked
- OpenAPI artifact generation/parse validation is visible in CI
- golden principles are documented and mechanically enforced for a stable subset
- nightly quality workflow runs with actionable artifacts without blocking PR-required gates

### Horizon I (Outreach CRM Deferred Expansion Track)

Focus:
- define an outreach CRM mode on top of existing board/starter-pack/proposal primitives
- start with card-first contact modeling (YAML front matter + timeline append pattern) before structured entity migration
- add deterministic cadence scheduling and draft-generation flows through proposal/chat infrastructure
- keep runtime behavior execution-mode configurable (manual/draft-first by default, connector execution as a separately gated layer)
- preserve low-priority sequencing so active foundation/security tracks remain ahead

Execution tracker and seeded issues:
- `#262` OUT-00 tracker
- `#263`, `#264`, `#265`, `#266`, `#267`, `#268`
- explicit reuse (no duplicate wave issue): `#75`, `#77`, `#175`, `#107`

Exit Criteria:
- outreach wave sequencing is documented and dependency-ordered in canonical planning docs
- issue backlog captures card-first model, contact UX, cadence engine, dashboard, and draft-generation slices
- overlapping import/analytics/starter-pack scope is reconciled through existing issues instead of duplicate seeds

## Active Backlog (Priority-Labeled)

### Priority I (Current Phase 4 Completion Path)

- Security and policy convergence: `#33`, `#34`, `#44`
- Final cross-user policy convergence follow-through: `#152`
- Starter packs foundation: `#48`, `#49`, `#50`, `#51` (delivered)
- Tech-debt blockers for stable expansion: `#52` (delivered), `#53` (delivered), `#54` (delivered)

### Priority II (Immediate Post-Phase-4 Foundation)

- Analysis follow-through wave tracker: `#151`
- Capture realignment wave: `#199` to `#211` (delivered); remaining linked hardening/performance follow-through: `#81`, `#212`, `#213`
- Testing harness guardrails wave tracker and delivery sequence: `#254` to `#260`
- Provider-agnostic LLM runtime expansion (`OpenAI` + `Gemini`) and demo setup hardening: `#232` (delivered)
- Managed-key LLM control-plane tracker and foundations: `#235`, `#236` (delivered), `#237`
- CI/workflow topology expansion and governance track: `#168`
- API/frontend hardening follow-through: `#153` (delivered), `#154` (delivered), `#155` (delivered), `#157` (delivered)
- Real-time and observability baseline: `#67` (delivered), `#68` (delivered)
- Container/deployment and performance harness baseline: `#69` (delivered), `#70` (delivered), `#142` (delivered)
- Multi-tenancy strategy and collaboration/integration foundations: `#71` (delivered), `#72` (delivered), `#73`, `#74`, `#75`, `#76` (delivered)

### Priority III (Expansion Tranche: Analytics, Security, Compliance, Premium UI Foundations)

- Analytics and forecasting: `#77`, `#78`, `#79`
- Security/compliance expansion: `#80`, `#81` (capture scope extended), `#82`, `#83`, `#106`, `#110`, `#156`, `#212`, `#238`, `#239`, `#240`
- Frontend premium UI foundations wave: `#242`, `#243`, `#244`, `#245`, `#246`, `#247`, `#248`, `#249`, `#250`
- Frontend premium wave reused dependencies: `#154` (lint/CI), `#88` (visual regression), `#92` (a11y remediation), `#213` (virtualization)

### Priority IV (Expansion Tranche: Platform, Test, UX, Docs Maturity)

- Platform and ops maturity: `#84`, `#85`, `#86`, `#101`, `#102`, `#103`, `#104`, `#105`, `#111`
- Test maturity: `#87`, `#88`, `#89`, `#90`, `#91`
- UX and onboarding maturity: `#92`, `#93`, `#94`, `#95`, `#96`
- Frontend responsiveness maturity: `#213`
- Optional premium UI documentation/component tooling: `#251`
- Developer/user docs maturity: `#99`, `#100`, `#216`, `#217`
- Deferred capture follow-ons after MVP retention proof: `#218`, `#219`, `#220`
- Outreach CRM deferred expansion wave: `#262` to `#268`
- Outreach CRM wave reused dependencies: `#75` (delivered import adapters), `#77` (analytics), `#175` (starter-pack catalog expansion)
- Codebase maintainability hotspot refactors (analysis wave): `#158`, `#159`, `#160`, `#161`, `#162`, `#163`, `#164`, `#165`, `#166`, `#167`

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
- Added capture/inbox realignment wave from `docs/InReview` planning packs with explicit dependency-mapped issue seeding (`#199` to `#213`).
- Added frontend premium UI foundations wave from `docs/InReview` premium UI pack with deduplicated issue mapping (`#242` to `#251`, reusing `#154`, `#88`, `#92`, `#213`).
- Added testing harness/guardrails wave from `docs/InReview` testing-harness pack with duplicate prevention for already-covered scenarios (`#254` to `#260`).
- Added outreach CRM deferred wave from `docs/InReview/outreach-crm` with low-priority issue seeding (`#262` to `#268`) and explicit reuse of overlapping existing issues (`#75`, `#77`, `#175`).

## Out-of-Code and Configuration Coverage Matrix

Covered by seeded issues:
- Docker + reverse proxy + compression baseline: `#69` (delivered)
- Developer MCP baseline and Docker Marketplace setup hardening: delivered (2026-02-20 local ops cycle)
- MCP operator wiring + verification workflow: `#140` (delivered via `#144`)
- MCP integration smoke/regression harness: `#141` (delivered)
- Staged rollout policy (blue/green/canary): `#101`
- IaC baseline: `#102`
- SBOM/release provenance: `#103`
- Cost guardrails: `#104`
- Backup/restore disaster recovery: `#86`
- OpenTelemetry metrics/tracing and alerting runbook: `#68`
- Load/concurrency harness and budgets: `#70` (delivered)
- Multi-tenancy strategy ADR: `#71` (delivered)
- API abuse/rate limiting: `#81`
- OWASP/security headers and CSRF/XSS baseline: `#80`
- Dependency vulnerability management policy: `#106`
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
4. Stage D (Priority III): security/compliance controls that reinforce tenant boundaries (`#80`, `#81`, `#82`, `#83`, `#110`).


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
## Next Best Steps (Immediate)

1. Close remaining unblocked Priority I security/policy work first (`#33`, `#34`, `#44`, `#152`) with regression coverage.
2. Sequence testing harness wave from the completed tracker handoff: `#255` -> (`#256`, `#257`) -> (`#258`, `#259`, `#260`), and keep aligned existing seeds `#89`, `#90`, `#106`, `#168`.
3. Sequence remaining managed-key control-plane foundations in Priority II: `#235` tracker, then `#237` after delivered identity attribution baseline (`#236`).
4. Sequence managed-key abuse/operations follow-through in Priority III: `#238`, `#239`, `#240`.
5. Start frontend premium UI wave with foundations-first ordering: `#243` -> `#245` -> `#244` -> (`#246`, `#247`, `#249`), then interaction/performance hardening `#248`, `#250`; keep reused dependencies `#154`, `#88`, `#92`, and `#213` synchronized.
6. Sequence capture-linked hardening by priority stage: `#81` and `#212` in Priority III, `#213` in Priority IV.
7. Keep issue `#107` synchronized as the single wave index and maintain one-priority-label-per-issue discipline (`Priority I` to `Priority V`).
8. Keep Outreach CRM expansion deferred in Priority IV and execute in dependency order when promoted: `#263`/`#264` -> `#265` -> `#266` -> (`#267`, `#268`), while reusing existing analytics/starter-pack tracks (`#77`, `#175`) and the delivered import-adapter foundation (`#75`).

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
