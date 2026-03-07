# Taskdeck Implementation Masterplan

Last Updated: 2026-03-07  
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
- Product legibility is now the immediate product focus: the app should explain its core loop from inside the UI, not mainly through docs and demo scripts.
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
   - Backend: 962 passing
   - Frontend unit: 478 passing
   - Default Playwright regression lane: 24 passing (`stakeholder-demo.spec.ts` remains opt-in/skipped by default)
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
   - linked follow-through status is now split: `#212` delivered the logging/telemetry redaction policy and runtime guardrails, while `#213` remains the pending performance/responsiveness slice
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
85. OPS-20 role discoverability and permission-guidance delivery (`#179`):
   - ops command permission failures now include current-role context, runnable-template fallback lists, and explicit next-step guidance to verify/request elevated access
   - ops console now surfaces current role and runnable-template discoverability context up front, and restricted template selection now shows explicit role-based warnings before run attempts
   - settings profile surface now includes role and ops-capability summaries, and operator/manual docs now codify the role-assignment workflow used for access elevation requests
86. UX-11 archive lifecycle control refinement (`#177`):
   - board settings lifecycle controls now use one explicit archive/restore action with deterministic confirmation messaging, replacing duplicate archive semantics in the same surface
   - archive workspace now supports hiding archived boards from the default list, explicit hidden-board reveal (`Show Hidden Boards`), and reversible unhide actions for clearer long-tail archive management
   - archive/frontend regression coverage now locks hidden-board visibility filtering behavior while API integration coverage locks archive/restore lifecycle transitions via board update contracts
87. SEC-05 OWASP baseline hardening (`#80`, delivered):
   - added API security-header middleware with explicit baseline headers (`Content-Security-Policy`, `X-Frame-Options`, `X-Content-Type-Options`, `Referrer-Policy`)
   - added environment-aware HSTS behavior (enabled for HTTPS, disabled by default in development unless explicitly configured)
   - added API integration coverage for header presence on success and auth-failure paths, plus HTTPS HSTS emission behavior in non-development hosting
   - published `docs/SECURITY_OWASP_BASELINE.md` with CSRF posture, OWASP checklist, and tracked follow-up security gaps
88. SEC-06 API rate-limiting and abuse-protection hardening (`#81`, delivered):
   - added partitioned fixed-window rate limiter policies for auth (`AuthPerIp`), capture create/triage (`CaptureWritePerUser`), and hot/costly paths (`HotPathPerUser`)
   - applied endpoint-level rate-limit policies across auth, capture, chat, and llm-queue write/stream surfaces
   - standardized throttle response contract (`429` + `ApiErrorResponse`) with deterministic retry diagnostics headers (`Retry-After`, `X-RateLimit-Policy`)
   - published operator tuning guidance and safe defaults in `docs/RATE_LIMITING_POLICY.md` with regression coverage for burst, reset-window recovery, and cross-user boundary behavior
   - follow-through hardening now supports trusted forwarded-header processing via explicit proxy/network allowlists and configurable forwarded-hop depth (`ForwardedHeaders:ForwardLimit`), while preserving no-trust defaults when allowlists are unset and documenting emergency/rollback plus proxy-topology smoke checks

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

Implementation carry-forward from the full source audit:

- treat workspace mode as durable product state; do not let it collapse into local-only view toggles once server-backed preferences become practical
- prefer aggregated product-shaped APIs for `Home`, `Today`, `Review`, and board summary needs over client-side fetch fan-out
- keep proposal summary generation in the application layer instead of forcing the frontend to reverse-engineer meaning from low-level operations
- keep board-aware action-rail behavior explicit (`Capture here`, `Ask assistant`, `Review proposals`, `Add card`) so board context actually travels
- preserve the secondary deferred set from the audit (`Demo Tools`, guided narrative/demo tour, nav badges, hero-board quality, HTML report, snapshot/trace assertions, presets/soak, replay-from-trace, scenario composer, saved views`) as later follow-through rather than letting it disappear

## Roadmap by Horizon

### Horizon A (Week 1 to 2): Novice-First Shell and Entry Clarity

Focus:
- add workspace mode preference (`guided`, `workbench`, `agent`) and persist it as durable product state
- add a true start surface (`Home`) instead of dropping every user into an implementation-shaped boards list
- make `Review` the primary normal-user automation surface and keep queue explicitly advanced
- replace dead-end empty states with action-oriented help blocks on primary pages
- replace raw board-ID happy paths with selectors/pickers in common flows
- prefer aggregate/product-shaped APIs for shell summaries instead of client-side stitching

Exit Criteria:
- a guided-mode user lands on a product-shaped entry surface
- the UI tells the user what to do first without requiring internal docs
- common capture/review/project flows do not require raw IDs
- queue remains available for power users but is no longer the implied default

### Horizon B (Week 3 to 6): Board-Centered Daily Workflow

Focus:
- add `Today` as a compact daily agenda surface
- add first-run onboarding checklist and project creation wizard
- add proposal summary service and readable proposal cards with plain-language summaries, risk, and deep links
- add board action rails so capture/chat/review follow the current board context by default (`Capture here`, `Ask assistant`, `Review proposals`, `Add card`)
- strengthen deep links across inbox, review, notifications, activity, and resulting boards/cards

Exit Criteria:
- the `capture -> review -> board` loop is visible and coherent inside the product
- board context travels without manual re-entry across primary surfaces
- a first-time user can create first value without wandering through operator pages
- proposal review feels like a product surface, not just a diff viewer

### Horizon C (Week 6 to 8): Docs, Help, and Verification Coherence

Focus:
- add a bridge doc (`START_HERE`) for first-run product understanding
- reshape the manual and index around top-level navigation and user goals
- add a required first-run golden-path smoke test
- define product-shaped telemetry and launch criteria for novice beta and later agent alpha
- treat the staged `novice-first-first-run` scenario shape as the acceptance target for the eventual first-run smoke path
- keep demo tooling as evidence and acceptance support rather than the main onboarding path

Exit Criteria:
- docs entry points match the product's intended top-level navigation
- the first-run smoke path covers `Home -> capture -> review -> execute -> board`
- novice users can recover from empty/confusing surfaces without leaving the product context
- launch criteria are explicit enough to guide seeding and release decisions

### Horizon D (Post-R1): Agent Substrate Foundation

Focus:
- add `AgentProfile`, `AgentRun`, and `AgentRunEvent` as first-class runtime primitives
- add a tool registry abstraction and policy evaluator
- add inspectable run traces and a first bounded agent template
- expose agent mode views only after the substrate is real

Exit Criteria:
- runs are first-class and inspectable
- agent behavior remains proposal-first and trace-first by default
- no opaque or silent autonomy is introduced

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

- managed-key LLM control plane and abuse controls: `#235`, `#237`, `#238`, `#239`, `#240`
- premium UI foundations and reskin wave: `#242` to `#250` (plus optional `#251`)
- long-list responsiveness and related UX scale follow-through: `#213`
- platform, ops, testing, and maturity backlog: `#84` to `#111`, `#87` to `#91`
- deferred outreach CRM expansion: `#262` to `#268`

## Active Backlog (Priority-Labeled)

### Priority I (Current Phase 4 Completion Path)

- Security and policy convergence: `#33`, `#34`, `#44`
- Final cross-user policy convergence follow-through: `#152`
- Starter packs foundation: `#48`, `#49`, `#50`, `#51` (delivered)
- Tech-debt blockers for stable expansion: `#52` (delivered), `#53` (delivered), `#54` (delivered)

### Priority II (Immediate Post-Phase-4 Foundation)

- Analysis follow-through wave tracker: `#151`
- Capture realignment wave: `#199` to `#211` (delivered); logging redaction follow-through `#212` is delivered, and remaining linked performance follow-through is `#213`
- Testing harness guardrails wave (`#254` to `#260`) is delivered; follow-up improvements now route through normal hardening issues
- Provider-agnostic LLM runtime expansion (`OpenAI` + `Gemini`) and demo setup hardening: `#232` (delivered)
- Managed-key LLM control-plane tracker and foundations: `#235`, `#236` (delivered), `#237`
- CI/workflow topology expansion and governance track: `#168`
- API/frontend hardening follow-through: `#153` (delivered), `#154` (delivered), `#155` (delivered), `#157` (delivered)
- Real-time and observability baseline: `#67` (delivered), `#68` (delivered)
- Container/deployment and performance harness baseline: `#69` (delivered), `#70` (delivered), `#142` (delivered)
- Multi-tenancy strategy and collaboration/integration foundations: `#71` (delivered), `#72` (delivered), `#73`, `#74`, `#75`, `#76` (delivered)
- Seeded Wave I from the 2026-03-07 MVP expansion integration:
  - `#318` tracker
  - `#320` workspace modes + `Home` summary shell
  - `#322` `Review`-first routing + empty/help states + board selectors
  - `#324` `Today` agenda + onboarding path
  - `#326` proposal readability + board-centered action flow
  - `#96` onboarding/contextual help (reused, moved to `Priority II`)
  - `#100` user guides/tutorials/FAQ (reused, moved to `Priority II`)
  - `#328` first-run smoke + launch-criteria guardrail
- Related but intentionally not folded into Wave I core execution: `#93`, `#216`, `#77`

### Priority III (Expansion Tranche: Analytics, Security, Compliance, Premium UI Foundations)

- Analytics and forecasting: `#77`, `#78`, `#79`
- Security/compliance expansion: `#80` (delivered), `#81` (delivered; capture scope extended), `#82`, `#83`, `#106`, `#110`, `#156`, `#212` (delivered), `#238`, `#239`, `#240`
- Frontend premium UI foundations wave: `#242`, `#243`, `#244`, `#245`, `#246`, `#247`, `#248`, `#249`, `#250`
- Frontend premium wave reused dependencies: `#154` (lint/CI), `#88` (visual regression), `#92` (a11y remediation), `#213` (virtualization)
- Planned seeding from the 2026-03-07 MVP expansion integration (not yet created as numbered issues):
  - agent workspace foundation (`AgentProfile`, `AgentRun`, `AgentRunEvent`, tool registry, policies, first agent template, agent views)
  - local-first knowledge and integrations product surface (`KnowledgeDocument`, SQLite FTS search, notes/transcript/clip intake, integrations registry page)
- Reuse-before-duplicate candidates for that later seeding pass: `#75`, `#97`, `#98`, `#218`, `#219`

### Priority IV (Expansion Tranche: Platform, Test, UX, Docs Maturity)

- Platform and ops maturity: `#84`, `#85`, `#86`, `#101`, `#102`, `#103`, `#104`, `#105`, `#111`
- Test maturity: `#87`, `#88`, `#89`, `#90`, `#91`
- UX and onboarding maturity: `#92`, `#93`, `#94`, `#95`
- Frontend responsiveness maturity: `#213`
- Optional premium UI documentation/component tooling: `#251`
- Developer/user docs maturity: `#99`, `#216`, `#217`
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
4. Stage D (Priority III): security/compliance controls that reinforce tenant boundaries (`#80`, `#81` delivered; `#82`, `#83`, `#110` pending).


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
- `docs/DEMO_EXPANSION_MIGRATION_SOT.md`
- `docs/temp_description.txt`
- `docs/ISSUE_EXECUTION_GUIDE.md`

Batch A baseline delivery (`#298`) status:
- baseline seeding command introduced (`npm run demo:seed`)
- v0-first-run UX defaults applied (advanced surfaces default off, Automations default to Proposals, queue composer instruction-first guidance)
- demo playbook promoted to active docs (`docs/DEMO_PLAYBOOK.md`)

Batch B harness/docs delivery (`#299`) status:
- reusable demo harness layer added (`npm run demo:run`, `npm run demo:autopilot`, `scripts/demo-lib.mjs`, `scripts/scenarios/*`)
- scenario modules added for engineering sprint, support triage, and content-calendar demo flows
- API walkthrough asset added: `demo/http/taskdeck-demo.http` (updated for current API contracts)
- stakeholder walkthrough recorder added as opt-in Playwright coverage (`tests/e2e/stakeholder-demo.spec.ts`, gated by `TASKDECK_RUN_DEMO=1`)
- demo operations docs expanded and indexed (`docs/DOGFOODING_GUIDE.md`, `docs/USER_MANUAL.md`, `docs/DEMO_PLAYBOOK.md`, `docs/INDEX.md`)

Batch C JSON/capture harness (`#300`) status:
- JSON scenario runner added with schema + sample scenarios (`scripts/scenario-json-runner.mjs`, `scripts/scenarios-json/*`)
- `demo:run` now prefers JSON scenarios and supports `--list`, `--skip-llm`, and `--continue-on-error`
- `demo:autopilot` now supports `--loop queue|capture|mixed` and capture controls (`--capture-prob`, `--leave-capture-untriaged-prob`, `--triage-timeout-ms`, `--capture-source`, `--capture-title-hint`)
- capture helper functions added in `scripts/demo-lib.mjs` and consumed by JSON runner/autopilot (`create/get/ignore/cancel/triage/wait-for-outcome`)
- scenario authoring/usage documentation added and indexed (`docs/SCENARIOS.md`, `docs/INDEX.md`, `docs/DEMO_PLAYBOOK.md`)

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

## Next Best Steps (Immediate)

1. Close remaining unblocked Priority I security/policy work first (`#33`, `#34`, `#44`, `#152`) with regression coverage.
2. Execute the seeded novice-first shell tranche in order: `#318` -> `#320` -> `#322`.
3. Execute the seeded board-centered daily workflow tranche immediately after shell foundations: `#324` -> `#326`.
4. Keep the docs/help/testing tranche synchronized with shipped behavior: `#96`, `#100`, then `#328`.
5. Keep the delivered testing-harness wave (`#254` to `#260`) in maintenance mode and route any new guardrail expansion through normal follow-up issues while keeping aligned existing seeds `#89`, `#90`, `#106`, and `#168`.
6. Continue managed-key control-plane and abuse follow-through in dependency order: `#235` -> `#237` -> `#238` / `#239` / `#240`.
7. Start frontend premium UI wave with foundations-first ordering: `#243` -> `#245` -> `#244` -> (`#246`, `#247`, `#249`), then interaction/performance hardening `#248`, `#250`; keep reused dependencies `#154`, `#88`, `#92`, and `#213` synchronized with the productization wave.
8. Keep agent substrate and knowledge/integrations work sequenced behind novice-first exit criteria; do not promote them ahead of Horizons A through C.
9. Keep issue `#107` synchronized as the single wave index and maintain one-priority-label-per-issue discipline (`Priority I` to `Priority V`).
10. Treat the demo-expansion migration wave (`#297` -> `#302`) as delivered; route any further demo-tooling work through normal scoped follow-up issues such as `#311` instead of reopening the migration batches.

## Documentation Operating Model
Active docs:
- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/TESTING_GUIDE.md`
- `docs/MANUAL_TEST_CHECKLIST.md`

Audience-first product docs:
- `docs/START_HERE.md`
- `docs/USER_MANUAL.md`
- `docs/DEMO_PLAYBOOK.md`

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
