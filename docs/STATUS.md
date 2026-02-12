# Taskdeck Status (Source of Truth)

Last Updated: 2026-02-12  
Status Owner: Repository maintainers  
Authoritative Scope: Current implementation, verified test execution, and active phase progress

## Project Summary

Taskdeck is a local-first Kanban system with a .NET 8 backend and a Vue 3 frontend.
Current implementation supports boards, columns, cards, labels, WIP rules, filters, keyboard workflows, drag/drop, toasts, and automation-oriented CLI output.
Multi-user/permissions, export-import, history/audit, and LLM queue capabilities are implemented as side-track service/API slices with JWT infrastructure, while full runtime enforcement and UI activation remain pending.

## Current Implementation Snapshot

### Backend

- Architecture: Clean Architecture (`Domain`, `Application`, `Infrastructure`, `Api`)
- API: REST controllers for boards, columns, cards, and labels
- Persistence: EF Core + SQLite
- Test layers:
  - domain unit tests
  - application/service unit tests
  - API integration tests (`Taskdeck.Api.Tests`)
  - CLI contract tests (`Taskdeck.Cli.Tests`)
- CLI track (`backend/src/Taskdeck.Cli`) includes:
  - `boards list|create|update`
  - `columns list|create`
  - `cards add|move|list`
  - `--json` mode (camelCase output) for automation callers
- Parallel scaffolding delivered (not active runtime behavior yet):
  - domain entities: `User`, `BoardAccess`, `AuditLog`, `LlmRequest`
  - board ownership field: `Board.OwnerId`
  - repository/service contracts for permissions, export/import, history, queue
  - infrastructure repositories and migration: `20260211082334_AddUserPermissionsAuditQueue`
- Service implementations delivered:
  - `UserService` - user CRUD with BCrypt password hashing
  - `AuthenticationService` - JWT login/register/validate with configuration safety checks
  - `AuthorizationService` - role-based permission checking with backward compatibility
  - `BoardAccessService` - board access grant/revoke/list with board-route and permission validation
  - `HistoryService` - audit log queries and action logging with limit/input validation
  - `LlmQueueService` - queue management and processing
  - `ExportImportService` - board export/import via JSON with export-shape import compatibility
- API controllers delivered:
  - `AuthController` - `/api/auth/login`, `/api/auth/register`, `/api/auth/change-password`
  - `UsersController` - `/api/users` CRUD endpoints
  - `BoardAccessController` - `/api/boards/{boardId}/access` management with board-bound access checks
  - `ExportController` - `/api/export/boards/{boardId}`, `/api/import/boards`
  - `LlmQueueController` - `/api/llm-queue` management
  - `AuditController` - `/api/audit` history endpoints
- JWT Bearer authentication middleware configured in Program.cs

### Frontend

- Stack: Vue 3 + TypeScript + Pinia + Vue Router + Vite
- Views: board list and board detail
- UX capabilities:
  - board/column/card/label CRUD
  - card edit modal, column edit modal, board settings modal, label manager
  - text/label/due-date/blocked filtering
  - keyboard shortcuts and shortcut help
  - card and column drag-and-drop
  - toast notifications
  - consistent `Escape` handling across modals and inline card forms
- E2E smoke suite includes:
  - board-column-card happy flow
  - filter panel toggle
  - WIP rejection flow
  - card move flow
  - board settings lifecycle
  - column reorder flow
  - keyboard-only open/close flow (`Enter`, `n`, `Escape`)
  - in-session filter persistence flow

## Phase Progress (Reconciled)

Progress is tracked against `filesAndResources/taskdeck_technical_design_document.md`.

1. Phase 1 - Core Data Model and API: COMPLETE (100%)
2. Phase 2 - Basic Web UI: COMPLETE (100%)
3. Phase 3 - UX Improvements: COMPLETE (100%)
4. Phase 4 - Advanced Features: IN PROGRESS (70%)
   Completed:
   - card and column drag/drop
   - CLI primary track expansion
   - CLI JSON output foundation
   - CI quality gates for backend unit, API integration, frontend unit, and E2E smoke
   - broader negative-path integration coverage
   - side-track scaffolding for multi-user/export-import/history/LLM queue
   - service implementations for authentication, authorization, board access, history, LLM queue, export/import
   - API controllers for all new services
   - JWT authentication middleware
   - unit tests for new service implementations (51 new tests)
   - merge-readiness hardening pass:
     - board-route scope checks for board access update/revoke
     - export/import JSON compatibility and permission checks
     - JWT configuration guards and inactive-user password-change protection
     - history and queue input validation hardening
   - additional automated coverage (+20 tests: 17 application, 3 API integration)
   Pending (primary track):
   - CI drift monitoring and reliability follow-through
   - CLI parity and JSON contract hardening
   - time tracking
   - analytics dashboard
   - recurring tasks
   Pending (side-track activation):
   - authentication/authorization enforcement on existing endpoints
   - frontend integration with auth/permissions
   - queue processing background service
   - audit logging integration into existing service operations

## Test Status (Reconciled and Verified)

Verification Date: 2026-02-12

### Backend Unit (Executed)

Commands:
- `dotnet test backend/Taskdeck.sln`

Result:
- Domain: 68/68 passing
- Application: 155/155 passing
- Backend Unit Total: 223/223 passing

### Backend Integration + CLI Contracts (Executed)

Commands:
- `dotnet test backend/Taskdeck.sln`

Result:
- API integration: 34/34 passing
- CLI contract: 4/4 passing
- Integration/Contract Total: 38/38 passing

### Frontend Unit (Executed)

Command:
- `cd frontend/taskdeck-web && npx vitest run`

Result:
- Component tests: 81/81 passing
- Store tests: 34/34 passing
- Frontend Unit Total: 115/115 passing

### Frontend E2E Smoke (Executed)

Command:
- `cd frontend/taskdeck-web && TASKDECK_E2E_DB=taskdeck.e2e.local.db npx playwright test`

Result:
- E2E smoke tests: 8/8 passing

### Total

- Combined automated total: 384/384 passing

## CI Status

- CI workflow: `.github/workflows/ci.yml`
- Gates and job split:
  - Backend Unit (domain + application + CLI contract)
  - API Integration
  - Frontend Unit
  - E2E Smoke (depends on all prior gates)
- Hardening:
  - unit/integration gates run on Ubuntu and Windows matrices
  - E2E smoke runs on Ubuntu
  - stale E2E DB cleanup is cross-platform safe (`node` removal command)

## Strategic Direction (LLM Automation)

Planned end goal is an AI-agent-compatible board with safe tool-driven automation:

- Local LLM agent can create/update/move/archive board items through stable interfaces.
- Input modes include direct text and voice transcript driven instructions.
- Proposed actions must be reviewable before apply (accept/edit/reject).
- Board should provide diff-like previews for proposed mutations.
- Security and fallback controls are required before autonomous execution modes.

Current state:
- proposal/approval/diff safety model remains primary design objective.
- queue/audit scaffolding exists but is not yet wired into active automation flows.

## Known Gaps and Risks

- CI first-run monitoring is partially blocked locally (`gh` CLI unavailable in this environment).
- E2E remains smoke-level; deeper regression/performance paths still need coverage.
- Agent-compatible safety model (proposal queue, approvals, audit trail, rollback) is still design-stage.
- JWT authentication is configured but not yet enforced across existing board/column/card/label endpoints.
- Current side-track endpoints still accept actor/user IDs via query/body; claim-based identity enforcement is pending full auth rollout.
- Boards created through legacy create-board flow still default to `OwnerId = null` for backward compatibility.
- Export/import database-level operations (ExportDatabaseAsync, ImportDatabaseAsync) are stubbed.
- LLM queue background processor is not yet implemented (manual ProcessNextRequestAsync available).
- Audit logging is not yet automatically integrated into existing board/card/column operations.

## Canonical Documentation Policy

- This file is the single source of truth for status and test numbers.
- `docs/IMPLEMENTATION_MASTERPLAN.md` is the single source for forward execution planning.
- `docs/MANUAL_TEST_CHECKLIST.md` is the canonical manual verification script.
- Deep-dive support docs (permissions, export/import, queue, scaffolding summary) are informational and must not override this file or the masterplan.
- Historical session and superseded planning notes live under `docs/archive/`.
