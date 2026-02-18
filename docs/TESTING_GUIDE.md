# Testing Guide

This is the active testing guide for Taskdeck.

Last Updated: 2026-02-18
Companion Active Docs:
- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/TESTING_GUIDE.md`
- `docs/MANUAL_TEST_CHECKLIST.md`

## Current Verified Totals (2026-02-18)

- Backend: 506/506 passing
  - Domain: 93
  - Application: 269
  - API integration: 136
  - CLI contract: 4
  - Architecture boundaries: 4
- Frontend unit: 271/271 passing
- Frontend E2E (smoke + automation/ops): 14/14 passing
- Combined automated total: 791/791 passing

## Backend Commands

Run full backend verification (recommended):

```bash
dotnet test backend/Taskdeck.sln -c Release -m:1
```

Run project-split backend verification:

```bash
dotnet test backend/tests/Taskdeck.Domain.Tests/Taskdeck.Domain.Tests.csproj -c Release
dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj -c Release
dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release
dotnet test backend/tests/Taskdeck.Cli.Tests/Taskdeck.Cli.Tests.csproj -c Release
dotnet test backend/tests/Taskdeck.Architecture.Tests/Taskdeck.Architecture.Tests.csproj -c Release
```

Note:
- If `Debug` runs fail with file-lock errors, stop running `Taskdeck.Api` processes or use `-c Release`.

## Frontend Unit + Build

```bash
cd frontend/taskdeck-web
npx vitest --run --reporter=verbose
npm run typecheck
npm run build
```

## Frontend E2E

Install browser once:

```bash
cd frontend/taskdeck-web
npx playwright install chromium
```

Run E2E suite:

```bash
cd frontend/taskdeck-web
npx playwright test --reporter=line
```

## CI Gates

Workflow: `.github/workflows/ci.yml`

- `docs-governance`
  - Enforces required active docs and docs index invariants
- `backend-architecture`
  - Enforces architecture boundaries in CI
- `backend-unit`
  - Domain + Application + CLI contract tests
  - Ubuntu and Windows matrix
- `api-integration`
  - API integration tests
  - Ubuntu and Windows matrix
- `frontend-unit`
  - Vitest + typecheck + build
  - Ubuntu and Windows matrix
- `e2e-smoke`
  - Playwright smoke + automation/ops flow
  - Ubuntu only
  - Depends on all prior gates

## Coverage Map

- Domain invariants:
  - `backend/tests/Taskdeck.Domain.Tests`
- Application services:
  - `backend/tests/Taskdeck.Application.Tests`
  - Includes board/card/column/label/auth/authorization/board-access/export-import/history/queue plus automation/archive/chat/ops/log services
- HTTP contracts and behavior mappings:
  - `backend/tests/Taskdeck.Api.Tests`
  - Includes core + automation/archive/chat/ops/log/health controllers
  - Includes `ResultExtensions` mapping tests for standardized API error/status behavior
- CLI contracts:
  - `backend/tests/Taskdeck.Cli.Tests`
- Architecture boundaries:
  - `backend/tests/Taskdeck.Architecture.Tests`
- Frontend unit behavior:
  - `frontend/taskdeck-web/src/tests`
  - Components, stores, API modules, composables, utilities
  - Includes shared utility tests for `queryBuilder` and `errorMessage`
- End-to-end journeys:
  - `frontend/taskdeck-web/tests/e2e`

## Manual Verification

Use `docs/MANUAL_TEST_CHECKLIST.md` for action-by-action manual validation.

## Development Sandbox Mode

For local development only, authorization bypass can be enabled via:
- `backend/src/Taskdeck.Api/appsettings.Development.json`
- `DevelopmentSandbox.Enabled = true`

Safety boundary:
- Sandbox bypass is forced off outside Development environment.
- Validation and data integrity rules still apply.
