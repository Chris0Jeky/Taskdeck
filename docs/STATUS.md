# Taskdeck Status (Source of Truth)

Last Updated: 2026-02-11  
Status Owner: Repository maintainers  
Authoritative Scope: Current implementation, verified test execution, and active phase progress

## Project Summary

Taskdeck is a personal, developer-focused Kanban application with a .NET 8 backend and Vue 3 frontend.
Core domain supports boards, columns, cards, labels, WIP limits, blocking, filtering, and drag-and-drop workflows.

## Current Implementation Snapshot

### Backend

- Architecture: Clean Architecture (`Domain`, `Application`, `Infrastructure`, `Api`)
- API: REST endpoints for boards, columns, cards, labels
- Persistence: EF Core + SQLite
- Test layers:
  - domain tests
  - application/service tests
  - API integration tests (`Taskdeck.Api.Tests`) using `WebApplicationFactory`
- CLI bootstrap added: `backend/src/Taskdeck.Cli`
  - `boards list|create`
  - `columns list`
  - `cards add|move`

### Frontend

- Stack: Vue 3 + TypeScript + Pinia + Vue Router + Vite
- Views: boards list and board detail
- UX features implemented:
  - CRUD flows for board/column/card/label
  - card edit modal, board settings, column edit, label manager
  - filter panel (text, label, due-date windows, blocked-only)
  - keyboard shortcuts + shortcut help modal
  - drag-and-drop cards and columns
  - toast notifications
- E2E baseline added:
  - Playwright smoke suite (`frontend/taskdeck-web/tests/e2e/smoke.spec.ts`)

## Phase Progress (Reconciled)

Progress is tracked against `filesAndResources/taskdeck_technical_design_document.md`.

1. Phase 1 - Core Data Model and API: COMPLETE (100%)
2. Phase 2 - Basic Web UI: COMPLETE (100%)
3. Phase 3 - UX Improvements: COMPLETE (100%)
4. Phase 4 - Advanced Features: STARTED (40%)
   Completed:
   - drag-and-drop for cards and columns
   - CLI track bootstrap (`Taskdeck.Cli`)
   Pending:
   - time tracking
   - analytics dashboard
   - recurring tasks
   - optional sync and multi-user tracks

## Test Status (Reconciled and Verified)

Verification Date: 2026-02-11

### Backend (Executed)

Command:
- `dotnet test backend/Taskdeck.sln`

Result:
- Domain: 42/42 passing
- Application: 87/87 passing
- API integration: 5/5 passing
- Backend Total: 134/134 passing

### Frontend Unit (Executed)

Command:
- `cd frontend/taskdeck-web && npx vitest run`

Result:
- Component tests: 77/77 passing
- Store tests: 34/34 passing
- Frontend Unit Total: 111/111 passing

### Frontend E2E Smoke (Executed)

Command:
- `cd frontend/taskdeck-web && TASKDECK_E2E_DB=taskdeck.e2e.local.db npx playwright test`

Result:
- E2E smoke tests: 2/2 passing

### Total

- Combined automated total: 247/247 passing

## CI Status

- CI workflow added: `.github/workflows/ci.yml`
- Gates included:
  - backend unit + integration (`dotnet test backend/Taskdeck.sln`)
  - frontend unit (`npx vitest run`)
  - E2E smoke (`npx playwright test`)

## Known Gaps and Risks

- API integration suite exists but is still minimal; coverage should expand to all critical error/edge flows.
- E2E suite currently smoke-level only; key business journeys still need deeper coverage.
- Frontend tooling warns about `baseline-browser-mapping` freshness; not blocking but should be cleaned up.

## Canonical Documentation Policy

- This file is the single source of truth for status and test numbers.
- `docs/IMPLEMENTATION_MASTERPLAN.md` is the single source for forward execution planning.
- Historical session and superseded planning notes live under `docs/archive/`.
