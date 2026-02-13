# Taskdeck Status (Source of Truth)

Last Updated: 2026-02-13  
Status Owner: Repository maintainers  
Authoritative Scope: Current implementation, verified test execution, and active phase progress

## Project Summary

Taskdeck is a local-first Kanban system with a .NET 8 backend and a Vue 3 frontend.
Core board workflows are stable, and advanced slices are implemented for automation proposals, chat, ops/log querying, archive recovery, and worker health reporting.

Current constraints are mostly hardening and consistency:
- security/identity behavior is not yet uniform across all controller families
- some UX/operator surfaces are functional but not yet keyboard-first or discoverability-first
- LLM flow is still mock-provider based

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
- Implemented automation stack:
  - `AutomationProposalService`, `AutomationPlannerService`, `AutomationPolicyEngine`, `AutomationExecutorService`
  - `ArchiveRecoveryService`
  - `ChatService` + `MockLlmProvider`
  - `OpsCliService` + `LogQueryService`
- Auth posture today:
  - JWT middleware is wired
  - `[Authorize]` currently enforced on chat, automation-proposals, archive, ops-cli, and logs controllers

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

## Phase Progress (Reconciled)

Progress is tracked against `filesAndResources/taskdeck_technical_design_document.md`.

1. Phase 1 - Core Data Model and API: COMPLETE (100%)
2. Phase 2 - Basic Web UI: COMPLETE (100%)
3. Phase 3 - UX Improvements: COMPLETE (100%)
4. Phase 4 - Advanced Features: IN PROGRESS (90%)

Completed in Phase 4:
- CI gate split and matrix hardening
- authn/authz infrastructure baseline
- export/import board JSON flow
- audit and queue service/API slices
- automation proposal lifecycle + diff + execute flow
- archive recovery flow
- chat + ops + logs + worker/health stack
- frontend integration for automations/chat/ops/archive

Remaining for Phase 4 completion:
- security and claim-based identity convergence across legacy controllers
- removal of query/body actor identity patterns where claims should be authoritative
- production-capable LLM provider path (or strict feature-gated mock-only policy)
- broader planner/executor coverage and safety semantics
- database-level export/import implementation
- UX and operator hardening for keyboard/accessibility/discoverability gaps

## Test Status (Executed)

Verification Date: 2026-02-13

### Backend (Executed)

Command:
- `dotnet test backend/Taskdeck.sln -c Release`

Result:
- Domain: 93/93 passing
- Application: 256/256 passing
- API integration: 86/86 passing
- CLI contract: 4/4 passing
- Backend Total: 439/439 passing

### Frontend Unit + Build (Executed)

Commands:
- `cd frontend/taskdeck-web && npx vitest --run --reporter=verbose`
- `cd frontend/taskdeck-web && npm run typecheck`
- `cd frontend/taskdeck-web && npm run build`

Result:
- Frontend unit: 238/238 passing
- Typecheck: passing
- Production build: passing

### Frontend E2E (Executed)

Command:
- `cd frontend/taskdeck-web && TASKDECK_E2E_DB=taskdeck.e2e.local2.db npx playwright test --reporter=line`

Result:
- E2E smoke + automation/ops flow: 11/11 passing

### Total

- Combined automated total: 688/688 passing

## CI Status

Workflow: `.github/workflows/ci.yml`

- `backend-unit` (Ubuntu/Windows)
- `api-integration` (Ubuntu/Windows)
- `frontend-unit` (Ubuntu/Windows)
- `e2e-smoke` (Ubuntu, depends on prior jobs)

## Known Gaps and Risks

Security and identity:
- legacy controller families are not yet fully aligned with claims-first identity handling
- mixed identity model (claims + query/body actor IDs) increases misuse risk

Automation and data:
- active LLM provider is mock-backed
- planner extraction remains rule/regex-based and intentionally narrow
- database-level export/import remains unimplemented

Observability and scalability:
- `LogQueryService` currently performs broad in-memory composition paths
- nullable warnings (`CS8618`) remain in newly added domain entities

UX and operability (reconciled from product notes):
- archive UX consistency for board archive/recovery is not fully cohesive
- command palette lacks full keyboard item selection/activation flow
- activity exploration relies on direct IDs (limited discoverability)
- ops/automation forms need stronger autocomplete/option scaffolding
- drag/edit interaction edge cases and escape-driven workspace exit ergonomics need hardening

## Recently Resolved (This Cycle)

- Archived non-authoritative feature/spec packs from active docs root into a dated archive bundle.
- Consolidated active docs to a smaller maintained set.
- Updated manual and automated testing documentation to current route surface and totals.

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
