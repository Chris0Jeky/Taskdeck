# Task: Add auditStore unit tests (real coverage, not demo)

- **GitHub Issue**: [#421](https://github.com/Chris0Jeky/Taskdeck/issues/421) (TST-CODEX-07)
- **Branch**: `test/auditStore-unit-tests`
- **Priority**: Tier 3 (medium)

## Source File

`frontend/taskdeck-web/src/store/auditStore.ts` (95 lines)

## Pattern File

`frontend/taskdeck-web/src/tests/store/captureStore.spec.ts` — follow this pattern for store testing with mocked API.

## Existing Demo-Only Tests (do NOT modify)

`frontend/taskdeck-web/src/tests/store/auditStore.demo.spec.ts` — these are demo-scenario tests, not functional unit tests.

## Test File to Create

`frontend/taskdeck-web/src/tests/store/auditStore.spec.ts`

## Test Cases

Read the store source to identify all actions and state, then test:

1. Initial state is empty/default
2. `fetchAuditLog` calls the correct API endpoint and populates state
3. `fetchAuditLog` with filters forwards filter parameters
4. Pagination state updates correctly on fetch
5. Error handling: API failure sets error state and does not corrupt existing data
6. Loading state transitions: loading=true during fetch, loading=false after

## Implementation Notes

- Mock the audit API module (likely `../../api/auditApi`)
- Use `createPinia()` + `setActivePinia()` for test isolation
- Follow the captureStore.spec.ts pattern for setup/teardown

## Verify

```bash
cd frontend/taskdeck-web && npx vitest --run -t "auditStore"
```

## Acceptance Criteria

- All test cases pass (alongside existing demo spec)
- No lint warnings
- Commit on branch, push, open PR linking the GitHub issue
