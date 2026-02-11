# Testing Guide

This is the active testing guide for Taskdeck.

## Current Verified Totals (2026-02-11)

- Backend: 134/134 passing
  - Domain: 42
  - Application: 87
  - API integration: 5
- Frontend unit: 111/111 passing
  - Store: 34
  - Components: 77
- Frontend E2E smoke: 2/2 passing
- Combined automated total: 247/247 passing

## Backend

Run all backend tests:

```bash
dotnet test backend/Taskdeck.sln
```

Run coverage:

```bash
dotnet test backend/Taskdeck.sln /p:CollectCoverage=true
```

## Frontend Unit (Vitest)

```bash
cd frontend/taskdeck-web
npx vitest run
```

List discovered Vitest test cases:

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

- Backend unit + API integration:
  - `dotnet test backend/Taskdeck.sln`
- Frontend unit:
  - `npx vitest run`
- E2E smoke:
  - `npx playwright test`

## Current Gaps

- API integration coverage is still minimal and should expand to more error and edge paths.
- E2E tests are currently smoke-level and should grow to cover high-risk flows (WIP, drag/drop, settings lifecycle).

## Test Writing Conventions

- Backend: xUnit + FluentAssertions + AAA pattern.
- Frontend unit: Vitest + Vue Test Utils.
- E2E: Playwright with deterministic selectors and resilient waits.
- Cover both success and failure paths, especially WIP and validation rules.
