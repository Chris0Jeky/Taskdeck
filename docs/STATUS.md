# Taskdeck Status (Source of Truth)

Last Updated: 2026-02-11  
Status Owner: Repository maintainers  
Authoritative Scope: Current implementation, verified test execution, and active phase progress

## Project Summary

Taskdeck is a local-first Kanban system with a .NET 8 backend and a Vue 3 frontend.
Current implementation supports boards, columns, cards, labels, WIP rules, filters, keyboard workflows, drag/drop, toasts, and automation-oriented CLI output.

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
- CLI track (`backend/src/Taskdeck.Cli`) now includes:
  - `boards list|create|update`
  - `columns list|create`
  - `cards add|move|list`
  - `--json` mode (camelCase output) for automation callers

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
- E2E smoke suite now includes:
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
4. Phase 4 - Advanced Features: IN PROGRESS (60%)
   Completed:
   - card and column drag/drop
   - CLI primary track expansion
   - CLI JSON output foundation
   - CI quality gates for backend unit, API integration, frontend unit, and E2E smoke
   - broader negative-path integration coverage
   Pending:
   - time tracking
   - analytics dashboard
   - recurring tasks
   - optional sync/multi-user tracks

## Test Status (Reconciled and Verified)

Verification Date: 2026-02-11

### Backend Unit (Executed)

Commands:
- `dotnet test backend/tests/Taskdeck.Domain.Tests/Taskdeck.Domain.Tests.csproj`
- `dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj`

Result:
- Domain: 42/42 passing
- Application: 87/87 passing
- Backend Unit Total: 129/129 passing

### Backend Integration + CLI Contracts (Executed)

Commands:
- `dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj`
- `dotnet test backend/tests/Taskdeck.Cli.Tests/Taskdeck.Cli.Tests.csproj`

Result:
- API integration: 31/31 passing
- CLI contract: 4/4 passing
- Integration/Contract Total: 35/35 passing

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

- Combined automated total: 287/287 passing

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

## Known Gaps and Risks

- CI first-run monitoring is partially blocked locally (`gh` CLI unavailable in this environment).
- E2E remains smoke-level; deeper regression/performance paths still need coverage.
- Agent-compatible safety model (proposal queue, approvals, audit trail, rollback) is still design-stage.

## Canonical Documentation Policy

- This file is the single source of truth for status and test numbers.
- `docs/IMPLEMENTATION_MASTERPLAN.md` is the single source for forward execution planning.
- `docs/MANUAL_TEST_CHECKLIST.md` is the canonical manual verification script.
- Historical session and superseded planning notes live under `docs/archive/`.
