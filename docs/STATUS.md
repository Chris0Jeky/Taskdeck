# Taskdeck Status (Source of Truth)

Last Updated: 2026-02-11  
Status Owner: Repository maintainers  
Authoritative Scope: Current implementation, verified test execution, and active phase progress

## Project Summary

Taskdeck is a local-first Kanban system with a .NET 8 backend and a Vue 3 frontend.
Current implementation supports boards, columns, cards, labels, WIP rules, filtering, keyboard workflows, drag/drop, and toasts.

## Current Implementation Snapshot

### Backend

- Architecture: Clean Architecture (`Domain`, `Application`, `Infrastructure`, `Api`)
- API: REST controllers for boards, columns, cards, and labels
- Persistence: EF Core + SQLite
- Test layers:
  - domain unit tests
  - application/service unit tests
  - API integration tests (`Taskdeck.Api.Tests`) with `WebApplicationFactory`
- CLI track (`backend/src/Taskdeck.Cli`) expanded to:
  - `boards list|create|update`
  - `columns list|create`
  - `cards add|move|list`

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
- E2E smoke suite expanded to critical journeys:
  - board-column-card happy flow
  - filter panel toggle shortcut
  - WIP rejection flow
  - card move between columns
  - board settings lifecycle (rename, archive/unarchive, delete)

## Phase Progress (Reconciled)

Progress is tracked against `filesAndResources/taskdeck_technical_design_document.md`.

1. Phase 1 - Core Data Model and API: COMPLETE (100%)
2. Phase 2 - Basic Web UI: COMPLETE (100%)
3. Phase 3 - UX Improvements: COMPLETE (100%)
4. Phase 4 - Advanced Features: IN PROGRESS (50%)
   Completed:
   - card and column drag/drop
   - CLI primary track started and expanded
   - CI quality gates for backend unit, API integration, frontend unit, and E2E smoke
   Pending:
   - time tracking
   - analytics dashboard
   - recurring tasks
   - optional sync/multi-user tracks

## Test Status (Reconciled and Verified)

Verification Date: 2026-02-11

### Backend (Executed)

Command:
- `dotnet test backend/Taskdeck.sln`

Result:
- Domain: 42/42 passing
- Application: 87/87 passing
- API integration: 17/17 passing
- Backend Total: 146/146 passing

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
- E2E smoke tests: 5/5 passing

### Total

- Combined automated total: 262/262 passing

## CI Status

- CI workflow: `.github/workflows/ci.yml`
- Gates and job split:
  - Backend Unit (domain + application)
  - API Integration
  - Frontend Unit
  - E2E Smoke (depends on all prior gates)
- Hardening:
  - unit/integration gates run on Ubuntu and Windows matrices
  - E2E smoke remains Ubuntu-targeted
  - stale E2E DB cleanup is cross-platform safe (`node` file removal)

## Strategic Direction (LLM Automation)

Planned end goal is an AI-agent-compatible board that supports tool-driven automation:

- Local LLM agent can create/update/move/archive board items via stable interfaces.
- Input modes include direct text and voice transcript driven instructions.
- Proposed actions must support human review before apply (accept/edit/reject).
- Change visibility should include clear diff-style previews for proposed board mutations.
- Security and fallback controls are required before autonomous execution modes.

## Known Gaps and Risks

- API integration coverage is now broader but still not exhaustive across all negative paths.
- E2E suite is still smoke-level; depth/performance/regression scenarios remain to be added.
- CLI behavior is implemented but lacks dedicated CLI-focused automated tests.
- Agent-compatible safety model (proposal queue, approvals, audit trail, rollback) is design-stage only.

## Canonical Documentation Policy

- This file is the single source of truth for status and test numbers.
- `docs/IMPLEMENTATION_MASTERPLAN.md` is the single source for forward execution planning.
- Historical session and superseded planning notes live under `docs/archive/`.
