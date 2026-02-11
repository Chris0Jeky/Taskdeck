# Testing Guide

This is the active testing guide for Taskdeck.

## Current Verified Totals (2026-02-11)

- Backend unit: 129/129 passing
  - Domain: 42
  - Application: 87
- Backend integration/contracts: 35/35 passing
  - API integration: 31
  - CLI contract: 4
- Frontend unit: 115/115 passing
  - Store: 34
  - Components: 81
- Frontend E2E smoke: 8/8 passing
- Combined automated total: 287/287 passing

## Backend Commands

Run backend unit tests:

```bash
dotnet test backend/tests/Taskdeck.Domain.Tests/Taskdeck.Domain.Tests.csproj
dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj
```

Run backend integration + CLI contract tests:

```bash
dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj
dotnet test backend/tests/Taskdeck.Cli.Tests/Taskdeck.Cli.Tests.csproj
```

Run full backend solution (optional convenience):

```bash
dotnet test backend/Taskdeck.sln
```

Note:
- If this command fails with file-lock errors, stop any running `Taskdeck.Api` process and rerun project-level commands above.

## Frontend Unit (Vitest)

```bash
cd frontend/taskdeck-web
npx vitest run
```

List discovered Vitest tests:

```bash
npx vitest list
```

## Frontend E2E Smoke (Playwright)

Install browser once:

```bash
cd frontend/taskdeck-web
npx playwright install chromium
```

Run smoke tests:

```bash
cd frontend/taskdeck-web
TASKDECK_E2E_DB=taskdeck.e2e.local.db npx playwright test
```

## CI Gates

Workflow: `.github/workflows/ci.yml`

- `backend-unit`
  - domain + application + CLI contract tests
- `api-integration`
  - API integration tests
- `frontend-unit`
  - Vitest suite
- `e2e-smoke`
  - Playwright smoke suite

Notes:
- `backend-unit`, `api-integration`, and `frontend-unit` run on Ubuntu and Windows matrices.
- `e2e-smoke` runs on Ubuntu and depends on all prior gates.

## Automation Coverage Map

- Domain rules and invariants:
  - `backend/tests/Taskdeck.Domain.Tests`
- Application service logic and branch coverage:
  - `backend/tests/Taskdeck.Application.Tests`
- HTTP contracts and error mapping:
  - `backend/tests/Taskdeck.Api.Tests`
- CLI automation contracts and JSON output:
  - `backend/tests/Taskdeck.Cli.Tests`
- Frontend component/state behaviors:
  - `frontend/taskdeck-web/src/tests`
- End-to-end critical journeys:
  - `frontend/taskdeck-web/tests/e2e`

## Manual Verification

Use `docs/MANUAL_TEST_CHECKLIST.md` for a complete manual action-by-action script with expected outcomes.

## Current Gaps

- CI run monitoring from local shell is limited here because `gh` CLI is unavailable.
- E2E coverage is still smoke-level and should continue expanding into deeper regression paths.

## Test Writing Conventions

- Backend: xUnit + FluentAssertions + AAA pattern.
- Frontend unit: Vitest + Vue Test Utils.
- E2E: Playwright with deterministic selectors and resilient waits.
- Cover both success and failure paths, especially validation, ownership checks, and WIP constraints.
