# Testing Guide

This is the active testing guide for Taskdeck.

## Current Verified Totals (2026-02-11)

- Backend: 129/129 passing
  - Domain: 42
  - Application: 87
- Frontend: 111/111 passing
  - Store: 34
  - Components: 77
- Combined: 240/240 passing

## Backend

Run all backend tests:

```bash
cd backend
dotnet test Taskdeck.sln
```

Run coverage:

```bash
dotnet test Taskdeck.sln /p:CollectCoverage=true
```

## Frontend

Run Vitest:

```bash
cd frontend/taskdeck-web
npm run test -- --run
```

If aggregate execution hangs in your local environment, run per file:

```bash
npx vitest run src/tests/components/BoardSettingsModal.spec.ts --reporter=basic
npx vitest run src/tests/components/CardModal.spec.ts --reporter=basic
npx vitest run src/tests/components/ColumnEditModal.spec.ts --reporter=basic
npx vitest run src/tests/components/FilterPanel.spec.ts --reporter=basic
npx vitest run src/tests/components/LabelManagerModal.spec.ts --reporter=basic
npx vitest run src/tests/store/boardStore.filtering.spec.ts --reporter=basic
npx vitest run src/tests/store/boardStore.spec.ts --reporter=basic
```

List all discovered frontend tests:

```bash
npx vitest list
```

## Gaps to Close

- API integration test project (`Taskdeck.Api.Tests`) not yet present
- E2E automation (Playwright/Cypress) not yet present

## Test Writing Conventions

- Backend: xUnit + FluentAssertions + AAA pattern
- Frontend: Vitest + Vue Test Utils
- Name tests for behavior, not implementation details
- Cover both success and failure paths, especially WIP rules and validation
