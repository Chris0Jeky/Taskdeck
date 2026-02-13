# Taskdeck Status (Source of Truth)

Last Updated: 2026-02-13  
Status Owner: Repository maintainers  
Authoritative Scope: Current implementation, verified test execution, and active phase progress

## Project Summary

Taskdeck is a local-first Kanban system with a .NET 8 backend and a Vue 3 frontend.
The core board workflow is stable (boards/columns/cards/labels, filters, keyboard flow, drag/drop), and the automation stack landed as working slices: archive recovery, automation proposals/execution, chat sessions, ops templates/logs, worker heartbeats, and readiness checks.

The current constraint is consistency and hardening, not feature absence:
- authorization is only partially enforced across controllers
- identity is still query/body-driven on several legacy endpoints
- LLM integration is mock-backed and planner parsing is rule-based
- docs have grown quickly and need tighter governance

## Current Implementation Snapshot

### Backend

- Architecture: Clean Architecture (`Domain`, `Application`, `Infrastructure`, `Api`)
- Persistence: EF Core + SQLite
- API controllers include:
  - Core: boards, columns, cards, labels
  - Access and identity: auth, users, board access
  - History/export/queue: audit, export/import, LLM queue
  - Automation/ops: automation proposals, archive, chat, ops CLI, logs, health
- Worker runtime:
  - `LlmQueueToProposalWorker` (queue polling -> planner/proposal flow, retries, heartbeat)
  - `ProposalHousekeepingWorker` (proposal expiry maintenance, heartbeat)
  - `WorkerHeartbeatRegistry` surfaced through `/health/ready`
- Automation services delivered:
  - `AutomationProposalService`, `AutomationPlannerService`, `AutomationPolicyEngine`, `AutomationExecutorService`
  - `ArchiveRecoveryService`
  - `ChatService` + `MockLlmProvider`
  - `OpsCliService` + `LogQueryService`
- Auth wiring:
  - JWT authentication middleware is enabled when valid settings are present
  - `[Authorize]` is currently applied to `ChatController`, `AutomationProposalsController`, `ArchiveController`, `OpsCliController`, and `LogsController`

### Frontend

- Stack: Vue 3 + TypeScript + Pinia + Vue Router + Vite
- Shell and routing:
  - workspace boards
  - activity views
  - automations (queue/proposals/chat)
  - ops (cli/endpoints/logs)
  - settings (profile/access/export-import)
  - archive
- API modules are in place for all currently shipped surfaces, including `automationApi`, `archiveApi`, `chatApi`, and `opsApi`
- Feature flags and request correlation (`X-Request-Id`) are integrated
- Current UX behavior:
  - automation proposals can be listed, diffed, approved/rejected/executed
  - chat sessions can produce proposal references
  - ops console runs templates and reads logs
  - archive view can list/restore archived entities

## Phase Progress (Reconciled)

Progress is tracked against `filesAndResources/taskdeck_technical_design_document.md`.

1. Phase 1 - Core Data Model and API: COMPLETE (100%)
2. Phase 2 - Basic Web UI: COMPLETE (100%)
3. Phase 3 - UX Improvements: COMPLETE (100%)
4. Phase 4 - Advanced Features: IN PROGRESS (90%)

Completed in Phase 4:
- CLI expansion and JSON contract baseline
- CI matrix and gate split (backend unit, API integration, frontend unit, E2E smoke)
- authn/authz infrastructure and board access model
- export/import board JSON flow (with compatibility import)
- audit and queue services/endpoints
- automation proposal lifecycle + diff preview + execution path
- archive recovery flow
- chat sessions/messages/stream endpoints with proposal handoff
- ops templates/runs/log querying endpoints
- background workers + heartbeat-aware readiness endpoint
- frontend automation/chat/ops/archive views and supporting API/state wiring

Remaining for Phase 4 completion:
- enforce auth and claims-based identity consistently across legacy controllers
- remove query/body `userId` acting-user patterns from endpoints that should use claims
- replace mock LLM path for production use (or formally gate it by environment/feature flag)
- expand planner/executor operation coverage and safety guarantees
- implement database-level export/import (currently returns not-implemented failures)
- finish documentation cleanup and drift controls

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

Notes:
- A `Debug` solution run initially failed due file locks from a running `Taskdeck.Api` process.
- Re-run in `Release` completed cleanly.

### Frontend Unit + Build (Executed)

Commands:
- `cd frontend/taskdeck-web && npx vitest --run --reporter=verbose`
- `cd frontend/taskdeck-web && npm run typecheck`
- `cd frontend/taskdeck-web && npm run build`

Result:
- Frontend unit: 238/238 passing
- Typecheck: passing
- Production build: passing

### Frontend E2E Smoke (Executed)

Command:
- `cd frontend/taskdeck-web && $env:TASKDECK_E2E_DB='taskdeck.e2e.local2.db'; npx playwright test --reporter=line`

Result:
- E2E smoke + automation ops flow: 11/11 passing

### Total

- Combined automated total: 688/688 passing

## CI Status

- Workflow: `.github/workflows/ci.yml`
- Gates:
  - `backend-unit` (Domain + Application + CLI) on Ubuntu/Windows
  - `api-integration` on Ubuntu/Windows
  - `frontend-unit` on Ubuntu/Windows
  - `e2e-smoke` on Ubuntu (depends on all previous gates)

## Strategic Direction (Automation)

Target remains proposal-first automation that is safe-by-default:
- users issue instructions through chat or other tooling
- system produces typed proposals and diff previews
- user approves/rejects before execution
- every operation is auditable and recoverable

Current state:
- end-to-end skeleton is functional (chat -> proposal -> execute)
- execution safety exists but is still bounded by mock LLM + limited parser coverage

## Known Gaps and Risks

- Auth coverage is partial:
  - legacy controllers (boards/columns/cards/labels/export/audit/queue/board-access/users) are not uniformly claim-enforced yet
- Identity handling is mixed:
  - several endpoints still accept actor/user IDs in query/body instead of deriving from claims
- LLM path is non-production:
  - `MockLlmProvider` is the active provider
  - planner parsing is regex/rule-based and supports a narrow instruction set
- Export/import scope gap:
  - `ExportDatabaseAsync` and `ImportDatabaseAsync` intentionally return not-implemented failures
- Audit consistency gap:
  - automation/archive flows emit audit entries, but legacy board/card/column/label mutations are not fully integrated into a single automatic audit pipeline
- Scalability risk in logs:
  - `LogQueryService` composes entries with broad in-memory reads and per-run expansion
- Build-quality signal gap:
  - nullable warnings (`CS8618`) are present in several newly introduced entities and should be resolved deliberately
- Documentation drift risk:
  - backend/frontend deep-dive packs are useful references but not continuously reconciled unless explicitly maintained

## Recently Resolved (This Cycle)

- Delivered automation proposal APIs and execution path with idempotency header support.
- Delivered archive listing and restore APIs with validation and permission checks.
- Delivered chat session/message/stream APIs and proposal handoff support.
- Delivered ops CLI and logs APIs with user scoping checks on run retrieval.
- Added queue-to-proposal and proposal-housekeeping workers plus heartbeat-aware readiness reporting.
- Expanded automated coverage significantly across backend services/controllers and frontend automation/ops APIs plus E2E automation flows.

## Canonical Documentation Policy

- `docs/STATUS.md` is the single source of truth for shipped behavior and verified test totals.
- `docs/IMPLEMENTATION_MASTERPLAN.md` is the single source of truth for active sequencing/priorities.
- `docs/MANUAL_TEST_CHECKLIST.md` remains canonical for manual validation steps.
- `docs/backend/*` and `docs/frontend/*` are reference specs/playbooks and may lag; they are non-authoritative unless reflected here.
- Historical or superseded planning artifacts belong in `docs/archive/`.

## Documentation Hygiene Rules (Effective Now)

- Any PR that changes behavior must update both:
  - `docs/STATUS.md` (what is true now)
  - `docs/IMPLEMENTATION_MASTERPLAN.md` (what changes next)
- Keep exactly two authoritative planning docs (`STATUS.md`, `IMPLEMENTATION_MASTERPLAN.md`).
- If a deep-dive spec is no longer actively maintained, archive it instead of letting it silently drift.
- Do not add new top-level planning docs unless they have a named owner and explicit review cadence.
