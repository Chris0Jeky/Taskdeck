# Taskdeck Status (Source of Truth)

Last Updated: 2026-02-23  
Status Owner: Repository maintainers  
Authoritative Scope: Current implementation, verified test execution, and active phase progress
Companion Active Docs:
- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/TESTING_GUIDE.md`
- `docs/MANUAL_TEST_CHECKLIST.md`

## Project Summary

Taskdeck is a local-first execution system for developers, built with a .NET 8 backend and a Vue 3 frontend.
Core board workflows are stable, and advanced slices are implemented for automation proposals, chat, ops/log querying, archive recovery, and worker health reporting.

Rebranding thesis (2026-02-23):
- capture should be near-zero friction
- automation should remain review-first and provenance-visible
- product value is reducing maintenance overhead, not maximizing opaque autonomy

Current constraints are mostly hardening and consistency:
- security and identity behavior is converging but still not uniform across all controller families
- some UX/operator surfaces are functional but not yet keyboard-first or discoverability-first
- LLM flow now supports feature-gated OpenAI usage, but defaults to mock for safe local/test posture
- MVP dogfooding flow now supports canonical checklist bootstrap in chat (proposal-first, board-scoped); broader template coverage remains future work
- collaborative editing now includes board/card presence visibility and conflict-hinting guardrails for stale card writes
- card collaboration now includes threaded comments with mention-linked notifications and moderation-aware edit/delete guardrails
- capture/inbox realignment has been backlog-seeded (`#199` to `#213`) but is not yet part of shipped runtime behavior

Target experience metrics for the capture direction (not yet verified as shipped):
- capture action to saved artifact should feel under 10 seconds in normal use
- capture artifact to reviewed/applicable proposal should be achievable inside a ~60-second loop

Direction guardrails (explicit):
- no silent/destructive automation by default
- keep proposal-first review gate for board mutations driven by capture triage
- preserve claims-first identity and stable error-contract behavior while expanding surface area

## Current Implementation Snapshot

### Backend

- Architecture: Clean Architecture (`Domain`, `Application`, `Infrastructure`, `Api`)
- Persistence: EF Core + SQLite
- Core controllers: boards, columns, cards, labels
- Extended controllers: auth, users, board-access, audit, export/import, llm-queue, automation proposals, archive, chat, notifications, ops-cli, logs, health, starter-packs
- Worker runtime:
  - `LlmQueueToProposalWorker`
  - `ProposalHousekeepingWorker`
  - `WorkerHeartbeatRegistry` (used by `/health/ready`)
- Cross-cutting API consistency:
  - `ApiErrorResponse` contract for stable error payload shape (`errorCode`, `message`)
  - `ResultExtensions` mapping for domain/app errors to HTTP statuses
  - JWT challenge/forbidden handlers return `ApiErrorResponse` payloads for middleware-level `401/403` responses
  - `AuthenticatedControllerBase` for claim extraction and authenticated-user guardrails
  - request correlation middleware (`X-Request-Id`) with response echo and log scope propagation
- Implemented automation stack:
  - `AutomationProposalService`, `AutomationPlannerService`, `AutomationPolicyEngine`, `AutomationExecutorService`
  - `ArchiveRecoveryService`
  - `ChatService` + deterministic `ILlmProvider` selection policy (`Mock` default; `OpenAI` behind explicit gates)
  - `NotificationService` with per-user preference filtering and deduplication safeguards
  - `OpsCliService` + `LogQueryService`
  - `StarterPackManifestValidator` + `StarterPackApplyService` (idempotent apply with dry-run conflict reporting)
  - SignalR realtime baseline: `BoardsHub` with board-scoped subscription authz and application-level board mutation event publishing
  - OpenTelemetry baseline for API + worker metrics/traces with configurable OTLP/console exporters
- Auth posture today:
  - JWT middleware is wired
  - `[Authorize]` currently enforced on boards, columns, cards, labels, export/import, audit, llm-queue, board-access, users, chat, notifications, automation-proposals, archive, ops-cli, and logs controllers

### Frontend

- Stack: Vue 3 + TypeScript + Pinia + Vue Router + Vite
- Workspace routes include:
  - boards
  - activity
  - automations (queue/proposals/chat)
  - notifications (inbox + read-state actions)
  - ops (cli/endpoints/logs)
  - settings (profile/preferences/access/export-import)
  - archive
- Feature slices integrated end to end:
  - proposal review/approve/reject/execute and diff viewing
  - chat session flow with proposal handoff
  - ops template execution and log querying
  - archive listing and restore operations
  - notification inbox and per-user notification preference controls
  - board realtime subscription lifecycle (SignalR join/leave/reconnect with polling fallback)
- Cross-cutting UI infrastructure:
  - command palette, feature flags, correlation IDs, toasts, keyboard shortcuts
- Shared maintainability utilities:
  - `buildQueryString` for API query construction across filter-driven endpoints
  - `getErrorMessage` for consistent API/store error extraction

## Phase Progress (Reconciled)

Progress is tracked against `filesAndResources/taskdeck_technical_design_document.md`.

1. Phase 1 - Core Data Model and API: COMPLETE (100%)
2. Phase 2 - Basic Web UI: COMPLETE (100%)
3. Phase 3 - UX Improvements: COMPLETE (100%)
4. Phase 4 - Advanced Features: IN PROGRESS (93%)

Completed in Phase 4:
- CI gate split and matrix hardening
- authn/authz infrastructure baseline
- boards controller family retrofit to claims-derived identity (`[Authorize]` + owner-scoped board operations)
- claims-first retrofit for columns/cards/labels/export-import/queue/board-access (actor identity derived from claims; caller actor query/body IDs removed)
- export/import board JSON flow
- audit and queue service/API slices
- automation proposal lifecycle + diff + execute flow
- archive recovery flow
- chat + ops + logs + worker/health stack
- frontend integration for automations/chat/ops/archive
- archive lifecycle coherence for boards across board settings and archive workspace flows
- drag/edit interaction safety guardrails via explicit card/column drag handles and non-handle drag blocking
- collaborative presence/conflict policy (`#73`): SignalR-backed board/card presence snapshots with editor markers, optimistic stale-write conflict handling, and conflict-audit capture with actor identity
- collaborative comments/mentions workflow (`#74`): board-scoped threaded card comments (create/list/reply/edit/delete), mention-to-user linking, mention notification publication, and authz-safe moderation boundaries
- maintainability refactor across API/controller error handling and frontend API/store utilities (PR #23)
- CI hardening follow-up: workflow concurrency cancellation, frontend typecheck/build parity, TRX artifacts, caching
- mechanical checks added: docs governance CI checks (`check-docs-governance` + `check-github-ops-governance`) and architecture boundary test project
- API integration harness additions for authz assertions (`AssertUnauthorized`, `AssertForbidden`, `AssertNotFoundOrForbidden`, `AssertCrossUserIsolation`)
- SEC-04 API error-contract assertions for key auth/validation paths, including middleware-level `401/403` payload normalization
- starter-pack manifest foundation (`PACK-01`): versioned manifest schema doc plus deterministic backend parsing/validation tests
- starter-pack apply backend (`PACK-02`): idempotent apply endpoint with dry-run conflict reporting and integration coverage for success/re-apply/conflict flows
- starter-pack frontend catalog (`PACK-03`): board-scoped catalog modal with search, preview (dry-run), and one-click apply flow with frontend interaction tests
- starter-pack first-party catalog (`PACK-04`): API-backed first-party pack catalog (label/column/blueprint packs) consumed by board starter-pack UI
- starter-pack deterministic fixture packs (`PACK-05`): Playwright bootstrap helpers and manifest-backed small/medium/edge deterministic E2E fixture coverage
- DEBT-01 nullability reduction (`#52`): domain `CS8618` warnings eliminated with EF-safe non-null initialization defaults
- DEBT-02 log-query scalability pass (`#53`): repository-filtered query flow replaces full-table scans and command-run log N+1 composition
- DEBT-03 database export/import (`#54`): sandbox-gated SQLite file export/import endpoints with payload signature/size validation and file-replacement rollback guardrails

Remaining for Phase 4 completion:
- UX/operator hardening for keyboard/accessibility/discoverability and escape-flow gaps

## Future Expansion Backlog Snapshot (2026-02-18)

Backlog seeding was expanded from near-horizon only to a staged future roadmap grounded in `docs/WIP` research PDFs.

- New future-expansion issues created: `#67` to `#111`
- Wave index issue: `#107` (`OPS-13`)
- Priority-label rollout completed across every issue (open and closed):
  - `Priority I`: current Phase 4 completion path
  - `Priority II`: post-Phase-4 foundation tranche
  - `Priority III`: analytics/security/compliance expansion tranche
  - `Priority IV`: platform, UX, testing, docs maturity tranche
  - `Priority V`: low-urgency/meta/historical tracking

Current open backlog is now split into:
- Phase-4 completion tranche (`#33` to `#57`, `Priority I`)
- Future expansion tranche (`#72` to `#111`, `Priority II` to `Priority V`)

## Analysis Follow-through Wave (2026-02-21)

To convert the 2026-02-21 repository scan into executable work, a dedicated issue wave was seeded:
- umbrella tracker: `#151`
- engineering hardening issues: `#152` to `#157`
- hotspot refactor issues: `#158` to `#167`
- CI/workflow topology expansion issue: `#168`

Priority distribution for this wave:
- `Priority I`: `#152`
- `Priority II`: `#151`, `#153`, `#154`, `#155`, `#157`, `#168`
- `Priority III`: `#156`
- `Priority IV`: `#158` to `#167`

Analysis record:
- `docs/analysis/2026-02-21_repo-scan-analysis.md`
- `docs/analysis/2026-02-21_ci-github-actions-expansion-plan.md`

## Capture Realignment Wave (2026-02-23)

Realignment packs under `docs/InReview` were reviewed and reconciled into active backlog seeding:
- automation realignment pack:
  - `docs/InReview/REPO_PACK/docs/analysis/2026-02-21_capture-automation_realignment_pack/`
- security/performance addendum:
  - `docs/InReview/docs/analysis/2026-02-21_capture-security-performance-addendum/`

Seeded issue wave:
- umbrella tracker: `#199`
- capture delivery sequence: `#200` to `#211`
- linked hardening/performance follow-through: `#212`, `#213`
- existing rate-limit issue updated with capture scope (no duplicate issue): `#81`
- deferred capture follow-ons seeded: `#218`, `#219`, `#220`
- adjacent go-to-market and research execution seeds: `#216`, `#217`

Implementation progress:
- `#200` CAP-01 implemented in current active development flow:
  - queue-wrapper capture model locked (`LlmRequest` + `inbox.capture.v1`)
  - capture source/status contracts and transition policy added
  - capture payload invariants enforced (schema version, text limits, actor-field rejection)
  - provenance linkage fields added to support `capture item -> triage run -> proposal`
- `#202` CAP-03 queue provenance fix implemented in active development:
  - planner now accepts explicit proposal source metadata overrides
  - queue worker now creates proposals with `SourceType = Queue`
  - queue worker forwards `SourceReferenceId` and `CorrelationId` using queue item id for traceability

Execution intent:
- preserve proposal-first trust posture (no direct model auto-apply)
- keep claims-first identity and `401/403/404` policy semantics
- require deterministic schema/error handling and provenance visibility for capture-generated changes

Reconciliation record:
- `docs/analysis/2026-02-23_capture-realignment-synthesis.md`
- `docs/analysis/2026-02-23_inreview-extraction-audit.md`
- `docs/analysis/2026-02-23_capture-model-decision.md`

## Test Status (Executed)

Verification Date: 2026-02-22

### Backend (Executed)

Command:
- `dotnet test backend/Taskdeck.sln -c Release -m:1`

Result:
- Domain: 93/93 passing
- Application: 357/357 passing
- API integration: 200/200 passing
- CLI contract: 4/4 passing
- Architecture boundaries: 8/8 passing
- Backend Total: 662/662 passing

### Frontend Unit + Build (Executed)

Commands:
- `cd frontend/taskdeck-web && npm run lint`
- `cd frontend/taskdeck-web && npm run test:coverage`
- `cd frontend/taskdeck-web && npm run typecheck`
- `cd frontend/taskdeck-web && npm run build`

Result:
- Frontend unit: 317/317 passing
- Coverage thresholds: passing
- Lint: passing
- Typecheck: passing
- Production build: passing

### Frontend E2E (Executed)

Command:
- `cd frontend/taskdeck-web && npx playwright test`

Result:
- E2E smoke + automation/ops + starter-pack fixture flow: 21/21 passing

### Total

- Combined automated total: 1000/1000 passing

## CI Status

Required workflow: `.github/workflows/ci-required.yml`

- `docs-governance` (Ubuntu)
- `backend-architecture` (Ubuntu)
- `backend-unit` (Ubuntu/Windows)
- `api-integration` (Ubuntu/Windows)
- `frontend-unit` (Ubuntu/Windows)
  - lint + typecheck + build + unit tests
- `container-images` (Ubuntu)
- `e2e-smoke` (Ubuntu, depends on prior jobs)

Extended/non-blocking workflow: `.github/workflows/ci-extended.yml`

- `workflow-lint` (Actionlint for workflow YAML drift)
- `dependency-review` (PR dependency risk check)
- label/manual-triggered backend solution + E2E smoke lanes (`testing` label or `workflow_dispatch`)
- label/manual-triggered load/concurrency harness lane via `.github/workflows/reusable-load-concurrency-harness.yml`

Nightly workflow: `.github/workflows/ci-nightly.yml`

- scheduled/manual backend solution regression
- scheduled/manual E2E smoke (reuses `.github/workflows/reusable-e2e-smoke.yml`)
- scheduled/manual load/concurrency harness (reuses `.github/workflows/reusable-load-concurrency-harness.yml`)
- scheduled/manual container image regression

Release/security workflow: `.github/workflows/release-security.yml`

- release/tag/manual dependency inventory + vulnerability signal artifacts
- optional strict frontend audit enforcement for manual runs
- container image artifact/checksum lane reused from container baseline workflow

## Known Gaps and Risks

Security and identity:
- claims-first identity is now aligned for boards/columns/cards/labels/export/queue/board-access
- claims-first identity is now aligned for audit/users as well (including self-scoped user/audit history flows)
- remaining security convergence work is concentrated on consistent cross-user policy enforcement breadth
- policy decision is now explicit: cross-user authenticated access failures should return `403`; remaining work is consistent enforcement across all families/tests

Automation and data:
- active LLM provider policy supports explicit mock vs OpenAI switching with safe defaults for development/test environments
- planner extraction remains rule/regex-based with deterministic validation and expanded board/column operation coverage
- database-level export/import now exists as a minimal safe implementation and is restricted to Development sandbox mode
- database import is file-replacement based and can fail when the SQLite file is actively locked by other operations; run imports during quiescent windows when possible
- capture inbox pipeline is planned but not shipped; key open dependencies are capture model/storage decision (`#200`), API surface (`#201`), triage worker path (`#204`), and end-to-end verification (`#210`)

Observability and scalability:
- frontend/CI baseline is now Node 24.13.1 (LTS) to align with Vite 7 engine requirements and longer support runway
- containerized deployment baseline is now shipped (`#69`): backend/frontend Dockerfiles, compose profile, reverse proxy compression/security headers posture, and CI image artifacts
- multi-tenancy strategy ADR is now documented (`#71`) with shared-schema + `TenantId` as the default rollout target; tenant isolation implementation slices remain pending
- local developer MCP posture now includes a Docker Marketplace server bundle with a stable default gateway set (`docker,docker-docs,openapi,time,jetbrains,filesystem,SQLite,terraform`) and optional integrations staged behind credentials/config (`postman`, `dockerhub`, `kubernetes`, `semgrep`)
- MCP operations runbook and helper scripts are now available for credential wiring and repeatable baseline/optional MCP dry-run verification
- MCP regression harness now provides actionable optional prerequisite diagnostics and CI-friendly status output modes (`PASS`, `PASS_WITH_WARNINGS`, `FAIL`)
- out-of-code/platform execution is now tracked, but not yet fully shipped:
  - production DB migration strategy (`#84`) and distributed cache strategy (`#85`)
  - backup/restore disaster-recovery playbook (`#86`)
  - staged rollout policy (`#101`), IaC baseline (`#102`), SBOM/provenance (`#103`), cost guardrails (`#104`)
  - cloud target topology and autoscaling ADR (`#111`)

UX and operability (reconciled from product notes):
- escape behavior now follows a top-surface-first contract; maintain regression coverage as new overlays and panels are introduced

Security/compliance hardening backlog added from research cross-check:
- OWASP/security headers + CSRF/XSS baseline (`#80`)
- API abuse/rate-limiting policy (`#81`)
- SSO/OIDC + optional MFA (`#82`)
- data portability/deletion workflow (`#83`)
- dependency vulnerability management policy (`#106`)
- secrets/configuration management baseline (`#110`)

## Recently Resolved (This Cycle)

- Unified API error-response shape and HTTP error-code mapping in shared backend helpers.
- Reduced duplicated frontend API/store logic by extracting shared query and error utilities.
- Reconciled active docs and test totals after PR #23 merge.
- Archived `REFACTOR_AUDIT_AND_ACTION_PLAN_2026-02-13.md` into `docs/archive/2026-02-13_phase4-doc-consolidation/audits-and-history/`.
- Added CI hardening parity updates: concurrency cancellation, frontend typecheck/build enforcement, TRX/JUnit failure artifacts, and package/browser caches.
- Delivered OPS-19 CI topology first pass (`#168`): migrated required pipeline entrypoint to `.github/workflows/ci-required.yml` and extracted docs-governance lane into reusable workflow `.github/workflows/reusable-docs-governance.yml`.
- Delivered OPS-19 CI topology second pass (`#168`): extracted backend architecture and frontend unit lanes into reusable workflows (`.github/workflows/reusable-backend-architecture.yml`, `.github/workflows/reusable-frontend-unit.yml`) and routed `ci-required.yml` through them.
- Delivered OPS-19 CI topology API-integration extraction (`#168`): extracted API integration lane into reusable workflow `.github/workflows/reusable-api-integration.yml` and routed `ci-required.yml` through it while preserving Ubuntu/Windows matrix behavior.
- Delivered OPS-19 CI topology third pass (`#168`): added `merge_group` trigger parity to `.github/workflows/ci-required.yml` so merge-queue evaluation runs the same required checks as PR/push.
- Delivered OPS-19 CI topology fourth pass (`#168`): extracted backend-unit lane into reusable workflow `.github/workflows/reusable-backend-unit.yml` and routed `ci-required.yml` through it while preserving Ubuntu/Windows matrix behavior and domain/application/CLI split coverage.
- Delivered OPS-19 CI topology fifth pass (`#168`): extracted container image and E2E smoke lanes into reusable workflows (`.github/workflows/reusable-container-images.yml`, `.github/workflows/reusable-e2e-smoke.yml`) and routed `ci-required.yml` through them while preserving required-gate dependencies and artifact behavior.
- Delivered OPS-19 CI topology sixth pass (`#168`): added non-blocking and scheduled orchestrator workflows (`.github/workflows/ci-extended.yml`, `.github/workflows/ci-nightly.yml`) plus release/security orchestration (`.github/workflows/release-security.yml`) and reusable full backend regression lane (`.github/workflows/reusable-backend-solution.yml`) to make nightly and release topology explicit.
- Added docs governance script and architecture boundary tests as CI invariants.
- Added GitHub operations governance script to enforce issue-template label hygiene and project-automation doc invariants in CI.
- Retrofitted boards controller family to claims-first authz with integration coverage for 401/403/cross-user/happy path.
- Retrofitted columns/cards/labels/export/queue/board-access to claims-first identity and removed caller-supplied actor query/body IDs.
- Added request-correlation middleware and propagated request IDs into Ops command correlation IDs.
- Added lightweight timing/result diagnostics for log queries and automation proposal execution.
- Recorded cross-user existence policy decision: use `403` for authenticated-but-unauthorized access, reserve `404` for true missing resources.
- Aligned active docs cross-links/date stamps across `STATUS`, `IMPLEMENTATION_MASTERPLAN`, `TESTING_GUIDE`, and `MANUAL_TEST_CHECKLIST`.
- Confirmed GitHub Project operational safety view as `No Status` (`no:status`) and documented release/weekly safety checks.
- Enforced `[Authorize]` on remaining legacy controllers (columns/cards/labels/export/audit/llm-queue/board-access/users) with expanded API integration `401` coverage.
- Retrofitted audit/users families to claims-first actor identity and self-scoped access with cross-user `403` coverage.
- Expanded authz regression matrix tests across legacy + advanced protected controllers for explicit `401/403/404` policy assertions.
- Advanced SEC-11 cross-user convergence (`#152`) with proposal-scope authorization enforcement in automation proposal lifecycle endpoints (`get/approve/reject/execute/diff`) and expanded API integration policy coverage for automation/logs/starter-pack protected routes.
- Advanced SEC-11 cross-user convergence (`#152`) with archive read-path authorization hardening: archive item list/detail/entity-lookup endpoints now enforce board-read permissions for the authenticated caller (`403` for cross-user unauthorized, `404` for true missing), with expanded application/API regression coverage.
- Advanced SEC-11 cross-user convergence (`#152`) with audit entity-history authorization hardening: `GET /api/audit/entities/{entityType}/{entityId}` now resolves board-scoped entities (`Board`/`Column`/`Card`/`Label`) and enforces board-read permissions (`403` cross-user unauthorized, `404` true missing), with expanded API regression matrix coverage.
- Advanced SEC-11 cross-user convergence (`#152`) with LLM queue board-scope authorization hardening: `POST /api/llm-queue` now enforces board-read permissions when `boardId` is provided (`403` cross-user unauthorized, `404` true missing board), with expanded application/API regression matrix coverage.
- Advanced SEC-11 cross-user convergence (`#152`) with final API coverage sweep: added explicit cross-user `403` assertions for board update, board-access management endpoints (`list/grant/update/revoke`), and chat session/message endpoints; added explicit chat `404` assertions for true missing session IDs.
- Delivered API-06 centralized exception/fallback error-contract hardening (`#153`): added global unhandled-exception middleware returning deterministic `ApiErrorResponse` (`UnexpectedError`) without internal exception leakage, standardized unknown-result fallback `500` mapping to the same contract shape, and added fault-injection API integration coverage asserting fallback payload shape plus correlation header expectations.
- Delivered TST-14 architecture-guard expansion (`#157`): added deterministic architecture invariants for source-layer purity (forbidden namespace imports in Domain/Application), controller boundary rules (`ControllerBase` direct inheritance restricted to auth/health controllers), and protected-controller `[Authorize]` declaration enforcement.
- Delivered AUTH-06 register/login hardening (`#174`) by preventing inactive-candidate short-circuit lockout in identifier-collision login paths, adding actionable duplicate-registration guidance, and expanding backend/frontend regression coverage for duplicate-register-then-login flow plus account-state vs invalid-credentials contract behavior.
- Delivered TST-01 load/concurrency regression harness (`#70`): added k6 board-heavy API profile with thresholds and diagnostics, added Playwright multi-session concurrency scenarios, and wired reusable load harness workflow into `ci-extended`/`ci-nightly` with artifact uploads.
- Delivered ARCH-01 multi-tenancy strategy ADR (`#71`): documented option tradeoffs (`database-per-tenant`, `schema-per-tenant`, `shared-schema + TenantId`), selected phased target model, and published tenant-isolation readiness + test strategy checklist.
- Delivered FE-11 frontend lint baseline + CI gate (`#154`): added Vue 3 + TypeScript ESLint baseline (`.eslintrc.cjs`), introduced `npm run lint` with zero-warning enforcement, integrated lint into reusable frontend CI workflow, and documented lint suppression guidance in active testing docs.
- Delivered FE-12 frontend coverage threshold gate (`#155`): enforced global + critical-surface Vitest coverage thresholds (`src/api`, `src/store`, `src/composables`, `src/utils`, `src/components/board`), switched required frontend CI lane to thresholded coverage execution, and standardized JUnit+coverage artifact upload for triage.
- Delivered COL-02 notification framework (`#72`): added notification domain/persistence + preferences model, shipped authenticated inbox/preferences/read-state APIs with preference-aware deduped event publication for mention/assignment/proposal-outcome families, integrated frontend inbox/preferences routes + stores, and expanded backend/frontend regression coverage.
- Delivered COL-04 card comments/mentions workflow (`#74`): added threaded card comments with reply constraints and moderation-aware edit/delete policy, integrated mention parsing with board-scope user linking and notification publication, shipped board/card comment APIs + frontend modal interactions, and expanded backend/frontend regression coverage.
- Standardized middleware-level auth failures to emit `ApiErrorResponse` payloads and added SEC-04 API integration assertions for auth + validation contract stability.
- Aligned board archive lifecycle UX/API contract: board settings archive action now reflects soft-delete semantics, archive workspace lists/restores archived boards, and API integration covers archive-to-restore roundtrip.
- Delivered UX-02 drag/edit interaction safety guardrails: card/column drag now starts from explicit handles only, and non-handle drag gestures are blocked with unit + E2E regression coverage.
- Delivered UX-03 command palette keyboard model: shell command palette now supports keyboard-first item filtering, selection, and activation with unit + E2E regression coverage.
- Delivered UX-04 activity selector discoverability: activity workflows now use selector-first board/entity/user exploration with ID copy affordance and unit + E2E regression coverage.
- Delivered UX-04 shared input-assist scaffolding: shared combobox/listbox input-assist is now integrated into Ops template selection and automation chat board targeting with keyboard-first option navigation and dedicated unit coverage.
- Delivered UX-05 escape behavior contract: Escape now closes only the top-most transient surface per key press, board routes exit to `/workspace/boards` when clean, and regression coverage spans shell/unit and board keyboard-flow E2E paths.
- Delivered AUTO-01 provider strategy: deterministic environment-aware `ILlmProvider` selection now gates OpenAI usage behind explicit config while keeping mock default safety, with policy + provider tests for switching behavior.
- Delivered AUTO-02 planner/executor hardening: expanded deterministic planner instruction coverage (board/column intents), hardened executor parameter validation and partial-failure semantics, and improved audit entity attribution with new regression coverage.
- Delivered MVP-01 chat-to-project bootstrap: canonical Markdown checklist paste now creates a proposal-first board bootstrap plan in chat, with one-click approve+execute path and regression coverage for happy path + key validation failures.
- Delivered PACK-01 starter-pack manifest foundation: added v1 manifest schema documentation and deterministic backend validator/test coverage for parsing, compatibility rules, and cross-reference validation.
- Delivered PACK-02 starter-pack apply backend: added `/api/boards/{boardId}/starter-packs/apply` with idempotent apply semantics, dry-run actionable conflict reporting, and API integration coverage for apply success/re-apply/conflict paths.
- Delivered PACK-03 starter-pack frontend catalog: added board-level starter pack catalog UI with search, preview (dry-run), and one-click apply flow, plus frontend API/component interaction tests.
- Delivered PACK-04 first-party starter packs v1: added API-backed first-party starter-pack catalog with common labels, common column flow, and 3 board blueprints, plus backend/frontend coverage for catalog usability and validity.
- Delivered PACK-05 deterministic fixture packs: added Playwright starter-pack fixture bootstrap helpers with manifest-backed small/medium/edge scenarios and dedicated E2E regression coverage.
- Delivered DEBT-01 nullability reduction: removed current domain `CS8618` warnings using EF-safe non-null default initialization patterns and verified backend regression suite pass.
- Delivered DEBT-02 log-query scalability pass: replaced broad in-memory + command-run N+1 log composition with repository-filtered query paths while preserving logs API behavior and contracts.
- Delivered COL-01 realtime board updates (`#67`): added authz-safe SignalR board subscriptions, app-layer mutation event publishing, frontend realtime lifecycle with polling fallback, and regression coverage across API/unit/E2E suites.
- Delivered OBS-01 observability baseline (`#68`): added OpenTelemetry tracing/metrics wiring, worker/queue/heartbeat telemetry emission, correlation-to-trace tagging, and versioned runbook/alert threshold documentation.
- Delivered OPS-07 containerized deployment baseline (`#69`): added production-oriented backend/frontend Dockerfiles, compose-based proxy stack with gzip/security header posture, CI image artifact packaging, and deployment runbook coverage.
- Expanded local Docker MCP Marketplace setup: enabled additional Docker catalog servers (including SQLite/JetBrains/Postman candidates), configured Docker gateway defaults in project Codex config, and documented optional credential-gated integrations.
- Added MCP operator runbook + scripts (`Set-MarketplaceMcpCredentials.ps1`, `Test-DockerMcpProfile.ps1`) for daily/weekly workflow integration and deterministic optional-server verification.
- Delivered TST-07 MCP integration smoke/regression harness (`#141`): optional-server prerequisite diagnostics are now explicit, strict/warning/skip policies are codified, and CI-friendly deterministic status output is documented and shipped.
- Seeded capture realignment wave issues (`#199` to `#213`), updated the wave index (`#107`) with a dedicated capture wave, and extended SEC-06 rate-limiting scope (`#81`) to include capture endpoints.
- Seeded future-expansion backlog issues (`#67` to `#111`) and added execution-wave index (`#107`).
- Applied `Priority I` through `Priority V` labels to every repository issue.

## Canonical Documentation Policy

Authoritative docs:
- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/TESTING_GUIDE.md`
- `docs/MANUAL_TEST_CHECKLIST.md`

Historical/spec detail material:
- `docs/archive/` (latest consolidation bundle: `docs/archive/2026-02-13_phase4-doc-consolidation/`)

Rule:
- If archive content conflicts with active docs, active docs win.
