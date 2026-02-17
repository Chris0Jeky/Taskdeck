# Taskdeck Status (Source of Truth)

Last Updated: 2026-02-17  
Status Owner: Repository maintainers  
Authoritative Scope: Current implementation, verified test execution, and active phase progress

## Project Summary

Taskdeck is a local-first Kanban system with a .NET 8 backend and a Vue 3 frontend.
Core board workflows are stable, and advanced slices are implemented for automation proposals, chat, ops/log querying, archive recovery, and worker health reporting.

Current constraints are mostly hardening and consistency:
- security and identity behavior is not yet uniform across all controller families
- some UX/operator surfaces are functional but not yet keyboard-first, discoverability-first, or interaction-conflict-safe
- LLM flow is still mock-provider based
- MVP dogfooding flow is incomplete: paste execution checklist in chat -> generate actionable board/project setup

## Current Implementation Snapshot

### Backend

- Architecture: Clean Architecture (`Domain`, `Application`, `Infrastructure`, `Api`)
- Persistence: EF Core + SQLite
- Core controllers: boards, columns, cards, labels
- Extended controllers: auth, users, board-access, audit, export/import, llm-queue, automation proposals, archive, chat, ops-cli, logs, health
- Worker runtime:
  - `LlmQueueToProposalWorker`
  - `ProposalHousekeepingWorker`
  - `WorkerHeartbeatRegistry` (used by `/health/ready`)
- Cross-cutting API consistency:
  - `ApiErrorResponse` contract for stable error payload shape (`errorCode`, `message`)
  - `ResultExtensions` mapping for domain/app errors to HTTP statuses
  - `AuthenticatedControllerBase` for claim extraction and authenticated-user guardrails
  - request correlation middleware (`X-Request-Id`) with response echo and log scope propagation
- Implemented automation stack:
  - `AutomationProposalService`, `AutomationPlannerService`, `AutomationPolicyEngine`, `AutomationExecutorService`
  - `ArchiveRecoveryService`
  - `ChatService` + `MockLlmProvider`
  - `OpsCliService` + `LogQueryService`
- Auth posture today:
  - JWT middleware is wired
  - `[Authorize]` currently enforced on boards, chat, automation-proposals, archive, ops-cli, and logs controllers

### Frontend

- Stack: Vue 3 + TypeScript + Pinia + Vue Router + Vite
- Workspace routes include:
  - boards
  - activity
  - automations (queue/proposals/chat)
  - ops (cli/endpoints/logs)
  - settings (profile/access/export-import)
  - archive
- Feature slices integrated end to end:
  - proposal review/approve/reject/execute and diff viewing
  - chat session flow with proposal handoff
  - ops template execution and log querying
  - archive listing and restore operations
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
4. Phase 4 - Advanced Features: IN PROGRESS (90%)

Completed in Phase 4:
- CI gate split and matrix hardening
- authn/authz infrastructure baseline
- boards controller family retrofit to claims-derived identity (`[Authorize]` + owner-scoped board operations)
- export/import board JSON flow
- audit and queue service/API slices
- automation proposal lifecycle + diff + execute flow
- archive recovery flow
- chat + ops + logs + worker/health stack
- frontend integration for automations/chat/ops/archive
- maintainability refactor across API/controller error handling and frontend API/store utilities (PR #23)
- CI hardening follow-up: workflow concurrency cancellation, frontend typecheck/build parity, TRX artifacts, caching
- mechanical checks added: docs governance CI checks (`check-docs-governance` + `check-github-ops-governance`) and architecture boundary test project
- API integration harness additions for authz assertions (`AssertUnauthorized`, `AssertForbidden`, `AssertNotFoundOrForbidden`, `AssertCrossUserIsolation`)

Remaining for Phase 4 completion:
- security and claim-based identity convergence across legacy controllers
- removal of query/body actor identity patterns where claims should be authoritative
- repository-wide enforcement of cross-user existence policy (`403` for authenticated-but-unauthorized access; `404` only for true missing resources)
- production-capable LLM provider path (or strict feature-gated mock-only policy)
- broader planner/executor coverage and safety semantics
- MVP chat-to-project bootstrap: paste checklist/plan text and generate a ready-to-use board via proposal-first flow
- database-level export/import implementation
- UX/operator hardening for keyboard/accessibility/discoverability/interaction-conflict gaps

## Test Status (Executed)

Verification Date: 2026-02-17

### Backend (Executed)

Command:
- `dotnet test backend/Taskdeck.sln -c Release -m:1`

Result:
- Domain: 93/93 passing
- Application: 259/259 passing
- API integration: 109/109 passing
- CLI contract: 4/4 passing
- Architecture boundaries: 4/4 passing
- Backend Total: 469/469 passing

### Frontend Unit + Build (Executed)

Commands:
- `cd frontend/taskdeck-web && npx vitest run`
- `cd frontend/taskdeck-web && npm run typecheck`
- `cd frontend/taskdeck-web && npm run build`

Result:
- Frontend unit: 245/245 passing
- Typecheck: passing
- Production build: passing

### Frontend E2E (Executed)

Command:
- `cd frontend/taskdeck-web && npx playwright test`

Result:
- E2E smoke + automation/ops flow: 11/11 passing

### Total

- Combined automated total: 725/725 passing

## CI Status

Workflow: `.github/workflows/ci.yml`

- `docs-governance` (Ubuntu)
- `backend-architecture` (Ubuntu)
- `backend-unit` (Ubuntu/Windows)
- `api-integration` (Ubuntu/Windows)
- `frontend-unit` (Ubuntu/Windows)
- `e2e-smoke` (Ubuntu, depends on prior jobs)

## Known Gaps and Risks

Security and identity:
- legacy controller families are not yet fully aligned with claims-first identity handling
- mixed identity model (claims + query/body actor IDs) increases misuse risk
- boards family is now claims-first; remaining legacy families are columns/cards/labels/export/audit/queue/board-access/users
- policy decision is now explicit: cross-user authenticated access failures should return `403`; remaining work is consistent enforcement across all families/tests

Automation and data:
- active LLM provider is mock-backed
- planner extraction remains rule/regex-based and intentionally narrow
- database-level export/import remains unimplemented

Observability and scalability:
- `LogQueryService` currently performs broad in-memory composition paths (now emits duration/result-size diagnostics)
- nullable warnings (`CS8618`) remain in domain entities
- local build environment is currently Node 22.11.0; Vite warns that 22.12+ is preferred (build still passes)

UX and operability (reconciled from product notes):
- archive board lifecycle behavior is not yet fully coherent with archive/recovery UX
- command palette lacks full keyboard item selection/activation flow
- activity exploration still relies on direct IDs (limited discoverability)
- ops/automation forms need stronger autocomplete/option scaffolding
- drag/edit interaction mode conflicts can still trigger unintended board/card movement
- escape-driven board/workspace exit ergonomics need a defined and test-backed model

## Recently Resolved (This Cycle)

- Unified API error-response shape and HTTP error-code mapping in shared backend helpers.
- Reduced duplicated frontend API/store logic by extracting shared query and error utilities.
- Reconciled active docs and test totals after PR #23 merge.
- Archived `REFACTOR_AUDIT_AND_ACTION_PLAN_2026-02-13.md` into `docs/archive/2026-02-13_phase4-doc-consolidation/audits-and-history/`.
- Added CI hardening parity updates: concurrency cancellation, frontend typecheck/build enforcement, TRX/JUnit failure artifacts, and package/browser caches.
- Added docs governance script and architecture boundary tests as CI invariants.
- Added GitHub operations governance script to enforce issue-template label hygiene and project-automation doc invariants in CI.
- Retrofitted boards controller family to claims-first authz with integration coverage for 401/403/cross-user/happy path.
- Added request-correlation middleware and propagated request IDs into Ops command correlation IDs.
- Added lightweight timing/result diagnostics for log queries and automation proposal execution.
- Recorded cross-user existence policy decision: use `403` for authenticated-but-unauthorized access, reserve `404` for true missing resources.

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
