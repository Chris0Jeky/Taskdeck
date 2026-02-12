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
- API: REST controllers for boards, columns, cards, labels, users, audit, export/import, LLM queue, board access, and authentication
- Persistence: EF Core + SQLite
- Domain validation: entity-level rules for name/title/description lengths, WIP limits, duplicate label guards, position bounds, hex color formats
- API error handling: consistent error code mapping across all controllers (NotFound → 404, ValidationError → 400, WipLimitExceeded → 400, AuthenticationFailed → 401, Forbidden → 403, Conflict → 409)
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
- Architecture: App shell with sidebar navigation, 19 routes, auth guards, feature flags
- Views: login, register, board list, board detail, settings/profile, board access, activity, automation queue, ops console, export/import, archive
- UX capabilities:
  - board/column/card/label CRUD
  - card edit modal, column edit modal, board settings modal, label manager
  - text/label/due-date/blocked filtering
  - keyboard shortcuts and shortcut help
  - card and column drag-and-drop
  - toast notifications
  - consistent `Escape` handling across modals and inline card forms
  - app shell with collapsible sidebar navigation
  - command palette (Ctrl/Cmd+K) with navigation commands
  - keyboard shortcuts help overlay (? key)
  - login/register/logout session management with JWT
  - route guards redirecting unauthenticated users to login
  - board access management panel (grant/revoke/role change)
  - audit timeline views (board/entity/user history)
  - LLM queue management (submit/cancel/process/stats)
  - automation proposals placeholder (pending backend)
  - ops console: CLI runner, endpoint explorer, logs viewer
  - board export (copy/download JSON) and import wizard
  - archive recovery placeholder (pending backend)
  - feature flags panel for enabling/disabling surfaces
  - design tokens (CSS custom properties) for consistent styling
  - ARIA landmarks and focus ring accessibility baseline
  - API request correlation IDs (`X-Request-Id`) for traceability
- API modules: boards, columns, cards, labels, auth, users, boardAccess, audit, queue, exportImport
- Pinia stores: board, toast, session, permissions, audit, queue, featureFlags
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
4. Phase 4 - Advanced Features: IN PROGRESS (80%)
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
   - additional automated coverage (+23 tests: 20 application, 3 API integration)
   - API error handling consistency: all GET endpoints now map error codes to proper HTTP status codes
   - domain validation hardening: description length limits (Card 2000, Board 1000 chars), duplicate label guard
   - SQLite DateTimeOffset ordering fix in AuditLogRepository and LlmQueueRepository with bounded SQL-side ordering/limits
   - comprehensive test expansion: +25 domain, +23 API integration, +73 frontend unit tests
   - frontend overhaul implementation:
     - app shell with sidebar navigation and command palette
     - auth views (login/register) and session management with JWT
     - 19-route workspace with auth guards and legacy redirects
     - board access management, audit timeline, queue management, ops console, export/import, archive UIs
     - design tokens, ARIA landmarks, feature flags system
     - 9 new type files, 6 new API modules, 5 new stores, 2 new composables
     - 73 new frontend unit tests (228 total frontend)
   Pending (primary track):
   - CI drift monitoring and reliability follow-through
   - CLI parity and JSON contract hardening
   - time tracking
   - analytics dashboard
   - recurring tasks
   Pending (side-track activation):
   - authentication/authorization enforcement on existing endpoints
   - queue processing background service
   - audit logging integration into existing service operations
   - backend endpoints for archive recovery, CLI bridge, log streaming, and automation proposals

## Test Status (Reconciled and Verified)

Verification Date: 2026-02-12

### Backend Unit (Executed)

Commands:
- `dotnet test backend/Taskdeck.sln`

Result:
- Domain: 93/93 passing
- Application: 162/162 passing
- Backend Unit Total: 255/255 passing

### Backend Integration + CLI Contracts (Executed)

Commands:
- `dotnet test backend/Taskdeck.sln`

Result:
- API integration: 57/57 passing
- CLI contract: 4/4 passing
- Integration/Contract Total: 61/61 passing

### Frontend Unit (Executed)

Command:
- `cd frontend/taskdeck-web && npx vitest run`

Result:
- Component tests: 81/81 passing
- Store tests: 80/80 passing (boardStore 14, boardStore.filtering 20, toastStore 14, sessionStore 7, featureFlagStore 7, permissionsStore 18)
- API layer tests: 37/37 passing (boardsApi 8, cardsApi 9, authApi 3, queueApi 6, boardAccessApi 4, auditApi 3, exportImportApi 4)
- Composable tests: 9/9 passing (useKeyboardShortcuts)
- Utility tests: 21/21 passing (jwt 4, queue 5, roles 5, requestId 3, navigation 4)
- Frontend Unit Total: 228/228 passing

### Frontend E2E Smoke (Executed)

Command:
- `cd frontend/taskdeck-web && TASKDECK_E2E_DB=taskdeck.e2e.local.db npx playwright test`

Result:
- E2E smoke tests: 8/8 passing

### Total

- Combined automated total: 552/552 passing

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
- Ownerless boards use transitional access bootstrap: first successful access grant claims ownership for `grantedBy`.
- Ownerless boards no longer allow implicit export reads; explicit board access is required.
- Export/import database-level operations (ExportDatabaseAsync, ImportDatabaseAsync) are stubbed.
- LLM queue background processor is not yet implemented (manual ProcessNextRequestAsync available).
- Audit logging is not yet automatically integrated into existing board/card/column operations.

## Recently Resolved Issues

- **SQLite DateTimeOffset ordering**: AuditLogRepository and LlmQueueRepository now use SQLite-targeted raw SQL with `ORDER BY` + `LIMIT` to preserve queue/history ordering without full in-memory scans.
- **API GET endpoint error handling**: All GET endpoints previously returned generic 500 for any error; now properly map error codes to HTTP status codes (404, 400, etc.).
- **Domain validation gaps**: Card descriptions now validate max length (2000 chars), Board descriptions validate max length (1000 chars), and duplicate label assignment on cards is prevented at the domain level.
- **Frontend build readiness**: Production frontend build now passes with strict app type-check (`npm run typecheck`) plus bundling (`npm run build`), and source-level nullability guards were added in board drag/drop and board store error handling.

## Practical Decisions and Tradeoffs (2026-02-12)

- **Decision**: Exclude `frontend/taskdeck-web/src/tests/**` from production `tsconfig.app.json` type-check scope.
  - **Why now**: keeps the merge gate focused on shipping code correctness while preserving runtime test validation via Vitest.
  - **Tradeoff**: TypeScript strictness for test fixtures is not currently enforced during `npm run build`.
  - **Future revision**: add a dedicated test type-check pipeline (`tsconfig.tests.json` + CI job), then ratchet test typing issues down incrementally.

- **Decision**: Centralize frontend API error message normalization in `boardStore` (`getErrorMessage`/`handleApiError`).
  - **Why now**: resolves repeated nullability issues and standardizes toast/error behavior quickly.
  - **Tradeoff**: parsing still uses lightweight shape checks, not a shared typed HTTP error abstraction.
  - **Future revision**: introduce a reusable API client error utility (or interceptor) used across all stores/modules.

- **Decision**: Use provider-gated SQLite raw SQL in queue/history repositories to keep ordering/limits at the database layer.
  - **Why now**: avoids runtime DateTimeOffset translation issues and removes O(n) dequeue/history scans.
  - **Tradeoff**: repository code now has provider-specific SQL branches (less portable than pure LINQ).
  - **Future revision**: converge on provider-agnostic LINQ by introducing a durable timestamp sort key/value converter strategy that translates cleanly across providers.

- **Decision**: Keep existing frontend/browser data warnings non-blocking (`baseline-browser-mapping` staleness, local Node `22.11.0` vs Vite recommended `22.12+`).
  - **Why now**: no functional breakage in build/test outcomes.
  - **Tradeoff**: environment drift risk remains.
  - **Future revision**: pin/upgrade Node in dev+CI and add scheduled dependency hygiene updates.

## Canonical Documentation Policy

- This file is the single source of truth for status and test numbers.
- `docs/IMPLEMENTATION_MASTERPLAN.md` is the single source for forward execution planning.
- `docs/MANUAL_TEST_CHECKLIST.md` is the canonical manual verification script.
- Deep-dive support docs (permissions, export/import, queue, scaffolding summary) are informational and must not override this file or the masterplan.
- Historical session and superseded planning notes live under `docs/archive/`.
