# Taskdeck Status (Source of Truth)

Last Updated: 2026-02-11
Status Owner: Repository maintainers
Authoritative Scope: Current implementation, verified tests, and active phase progress

## Project Summary

Taskdeck is a personal, developer-focused Kanban application with a .NET 8 backend and Vue 3 frontend.
Core domain supports boards, columns, cards, labels, WIP limits, blocking, filtering, and drag-and-drop workflows.

## Current Implementation Snapshot

### Backend

- Architecture: Clean Architecture (`Domain`, `Application`, `Infrastructure`, `Api`)
- API: REST endpoints for boards, columns, cards, labels
- Persistence: EF Core + SQLite
- Key behavior: WIP enforcement, card movement/reordering, column reordering endpoint, result-based error handling

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

## Phase Progress (Reconciled)

Progress is tracked against the original roadmap in `filesAndResources/taskdeck_technical_design_document.md`.

1. Phase 1 - Core Data Model and API: COMPLETE (100%)
2. Phase 2 - Basic Web UI: COMPLETE (100%)
3. Phase 3 - UX Improvements: MOSTLY COMPLETE (85%)
   Remaining gaps:
   - keyboard shortcuts call TODO handlers for open/create card actions in `frontend/taskdeck-web/src/views/BoardView.vue`
4. Phase 4 - Advanced Features: STARTED (25%)
   Complete:
   - drag-and-drop for cards and columns
   Pending:
   - time tracking
   - analytics dashboard
   - CLI client
   - optional sync/multi-user tracks

## Test Status (Reconciled and Verified)

Verification Date: 2026-02-11

### Backend (Executed)

Command:
- `dotnet test backend/Taskdeck.sln`

Result:
- Domain: 42/42 passing
- Application: 87/87 passing
- Backend Total: 129/129 passing

### Frontend (Executed)

Commands executed per test file (Vitest):
- `npx vitest run src/tests/components/BoardSettingsModal.spec.ts --reporter=basic`
- `npx vitest run src/tests/components/CardModal.spec.ts --reporter=basic`
- `npx vitest run src/tests/components/ColumnEditModal.spec.ts --reporter=basic`
- `npx vitest run src/tests/components/FilterPanel.spec.ts --reporter=basic`
- `npx vitest run src/tests/components/LabelManagerModal.spec.ts --reporter=basic`
- `npx vitest run src/tests/store/boardStore.filtering.spec.ts --reporter=basic`
- `npx vitest run src/tests/store/boardStore.spec.ts --reporter=basic`

Result:
- Component tests: 77/77 passing
- Store tests: 34/34 passing
- Frontend Total: 111/111 passing

Additional inventory check:
- `npx vitest list` reports 111 declared test cases.

### Total

- Combined Total: 240/240 passing

## Known Gaps and Risks

- No backend API integration test project (`Taskdeck.Api.Tests`) yet.
- No frontend E2E suite (Playwright/Cypress) yet.
- Several historical docs contain outdated counts and were archived.

## Canonical Documentation Policy

- This file is the single source of truth for status and test numbers.
- `IMPLEMENTATION_MASTERPLAN.md` is the single source for forward execution planning.
- All historical session and legacy planning notes live under `docs/archive/`.
