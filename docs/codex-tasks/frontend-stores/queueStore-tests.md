# Task: Add queueStore unit tests (real coverage, not demo)

- **GitHub Issue**: [#422](https://github.com/Chris0Jeky/Taskdeck/issues/422) (TST-CODEX-08)
- **Branch**: `test/queueStore-unit-tests`
- **Priority**: Tier 3 (medium)

## Source File

`frontend/taskdeck-web/src/store/queueStore.ts` (165 lines)

## Pattern File

`frontend/taskdeck-web/src/tests/store/captureStore.spec.ts`

## Existing Demo-Only Tests (do NOT modify)

`frontend/taskdeck-web/src/tests/store/queueStore.demo.spec.ts`

## Test File to Create

`frontend/taskdeck-web/src/tests/store/queueStore.spec.ts`

## Test Cases

Read the store source to identify all actions and state, then test:

1. Initial state is empty/default
2. Queue item fetching calls correct API and populates state
3. Queue item submission forwards payload to API
4. Status transitions update correctly in state
5. Error handling on API failure
6. Loading state management during async operations
7. Any computed getters return expected values from state

## Implementation Notes

- Mock the queue/LLM API module
- Use `createPinia()` + `setActivePinia()` for test isolation

## Verify

```bash
cd frontend/taskdeck-web && npx vitest --run -t "queueStore"
```

## Acceptance Criteria

- All test cases pass (alongside existing demo spec)
- No lint warnings
- Commit on branch, push, open PR linking the GitHub issue
