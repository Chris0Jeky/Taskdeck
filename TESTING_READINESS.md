# Testing and Development Posture Review

## Current context
- Backend: .NET 8 API with clean architecture, SQLite persistence, and 42 passing domain tests; application/service tests not yet written.【F:README.md†L1-L113】【F:IMPLEMENTATION_STATUS.md†L103-L180】
- Frontend: Vue 3 + Vite + Pinia app with basic boards/board view UI; no automated frontend tests are in place.【F:README.md†L55-L120】【F:IMPLEMENTATION_STATUS.md†L182-L248】【F:IMPLEMENTATION_STATUS.md†L451-L491】
- Existing test planning: TEST_SUITE_PLAN.md outlines a full pyramid (unit, integration, E2E) with detailed backlogs for backend services, API integration, Vitest setup, and Playwright E2E scenarios, but most are still aspirational.【F:TEST_SUITE_PLAN.md†L1-L120】【F:TEST_SUITE_PLAN.md†L240-L373】【F:TEST_SUITE_PLAN.md†L492-L606】

## How to exercise current functionality today
1. **Backend smoke**: `cd backend && dotnet test` (validates the existing 42 domain tests).【F:README.md†L123-L149】
2. **Run API locally**: `cd backend && dotnet run --project src/Taskdeck.Api/Taskdeck.Api.csproj` and verify Swagger at `http://localhost:5000/swagger` for endpoint sanity checks.【F:README.md†L15-L74】
3. **Frontend manual pass**: `cd frontend/taskdeck-web && npm install && npm run dev`, then walk through creating a board, adding columns/cards, and confirming state updates against the local API.【F:README.md†L76-L121】

## Near-term testing actions (1–2 sessions)
- **Backfill application/service tests**: Implement the high-priority suites described in TEST_SUITE_PLAN.md for BoardService, CardService (WIP enforcement and label handling), ColumnService, and LabelService; target 80%+ coverage for `Taskdeck.Application` before adding new features.【F:TEST_SUITE_PLAN.md†L240-L373】
- **API integration harness**: Create a `Taskdeck.Api.Tests` project using `WebApplicationFactory<Program>` to cover CRUD, validation, and WIP-limit flows end-to-end against an in-memory SQLite database.【F:TEST_SUITE_PLAN.md†L374-L456】
- **Frontend unit setup**: Add Vitest + vue-test-utils to `frontend/taskdeck-web`, start with store tests for `boardStore` and small component tests for column/card rendering and error states.【F:TEST_SUITE_PLAN.md†L494-L566】
- **Critical E2E path**: Stand up Playwright tests for board creation and the basic Kanban workflow; align with the existing scenario outlines to ensure parity between manual QA and automation.【F:TEST_SUITE_PLAN.md†L568-L653】

## Medium-term posture improvements
- **Unified fixtures**: Introduce builders/factories for boards, columns, cards, and labels shared across backend unit/integration tests to reduce duplication (as hinted in TEST_SUITE_PLAN.md).【F:TEST_SUITE_PLAN.md†L334-L373】
- **Deterministic test data**: Use seeded SQLite files or in-memory DB initialization scripts for repeatable API/integration runs; consider snapshotting base datasets for E2E bootstrapping.
- **CI enablement**: Add GitHub Actions (or equivalent) to run backend unit tests, backend integration tests, frontend unit tests, and Playwright smoke flows on each push; publish coverage artifacts and fail on regressions.
- **Quality gates**: Require `dotnet test` and `npm run test` checks to pass before merging; add linting (ESLint/Prettier) and TypeScript `--noEmit` to the pipeline to catch typing issues early.
- **Observability for QA**: Wire minimal logging/assertable metrics in tests (e.g., count of returned cards, WIP limit messages) and expose feature flags for upcoming UX changes to keep tests stable while UX evolves.

## Recommended order of execution
1. Stabilize backend service layer tests (fast feedback, highest business risk coverage).
2. Layer in API integration tests to lock down contracts used by the frontend.
3. Stand up frontend unit tests to protect rendering and store logic.
4. Add Playwright happy-path flows and promote them to CI once green locally.

## Definition of done for testing posture upgrade
- Automated suites exist for domain, application, API, frontend unit, and two key E2E flows.
- CI runs all suites with coverage thresholds: Domain 90%, Application 80%, API 70%, Frontend 70%.
- New features cannot merge without green checks and updated test cases documenting new behaviors.
