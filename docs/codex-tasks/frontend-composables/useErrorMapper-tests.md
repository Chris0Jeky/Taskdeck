# Task: Add useErrorMapper unit tests

- **GitHub Issue**: [#418](https://github.com/Chris0Jeky/Taskdeck/issues/418) (TST-CODEX-04)
- **Branch**: `test/useErrorMapper-unit-tests`
- **Priority**: Tier 2 (easy)

## Source File

`frontend/taskdeck-web/src/composables/useErrorMapper.ts` (44 lines, 3 exported functions)

## Pattern File

`frontend/taskdeck-web/src/tests/composables/useWorkspaceHelp.spec.ts`

## Test File to Create

`frontend/taskdeck-web/src/tests/composables/useErrorMapper.spec.ts`

## Test Cases

### `mapErrorToMessage`
1. Returns correct message for known error code (`ValidationError` -> `Please check your input...`)
2. Returns correct message for `NotFound`, `AuthenticationFailed`, `Forbidden`, `Conflict`, `WipLimitExceeded`
3. Returns `UnexpectedError` message for unknown error codes
4. Returns error's own message if present, before falling back to code lookup

### `parseApiError`
5. Extracts errorCode and message from `{ response: { data: { errorCode, message } } }`
6. Returns null for non-object input (null, undefined, string, number)
7. Returns null when `response.data.errorCode` is missing

### `getErrorDisplay`
8. Returns API error message and code when error is parseable
9. Falls back to `err.message` string when not an API error shape
10. Returns the fallback string when error has no usable message

## Verify

```bash
cd frontend/taskdeck-web && npx vitest --run -t "useErrorMapper"
```

## Acceptance Criteria

- All 10 test cases pass
- No lint warnings
- Commit on branch, push, open PR linking the GitHub issue
