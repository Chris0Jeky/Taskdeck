# Testing Guide

This is the active testing guide for Taskdeck.

## Current Verified Totals (2026-02-11)

- Backend: 146/146 passing
  - Domain: 42
  - Application: 87
  - API integration: 17
- Frontend unit: 111/111 passing
  - Store: 34
  - Components: 77
- Frontend E2E smoke: 5/5 passing
- Combined automated total: 262/262 passing

## Backend

Run all backend tests:

```bash
dotnet test backend/Taskdeck.sln
```

Run unit-only projects:

```bash
dotnet test backend/tests/Taskdeck.Domain.Tests/Taskdeck.Domain.Tests.csproj
dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj
```

Run API integration only:

```bash
dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj
```

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
  - domain + application unit tests
- `api-integration`
  - API integration tests
- `frontend-unit`
  - Vitest suite
- `e2e-smoke`
  - Playwright smoke suite

Notes:
- `backend-unit`, `api-integration`, and `frontend-unit` run on Ubuntu and Windows matrices.
- `e2e-smoke` currently runs on Ubuntu and depends on all prior gates.

## Current Gaps

- API integration has broader coverage now, but still lacks a few edge-case mappings.
- E2E remains smoke-level and should expand to deeper regression coverage.
- CLI command behavior is not yet covered by dedicated automated tests.

## Test Writing Conventions

- Backend: xUnit + FluentAssertions + AAA pattern.
- Frontend unit: Vitest + Vue Test Utils.
- E2E: Playwright with deterministic selectors and resilient waits.
- Cover both success and failure paths, especially validation and WIP constraints.
