# Task: Add usersApi unit tests

- **GitHub Issue**: [#417](https://github.com/Chris0Jeky/Taskdeck/issues/417) (TST-CODEX-03)
- **Branch**: `test/usersApi-unit-tests`
- **Priority**: Tier 1 (trivial)

## Source File

`frontend/taskdeck-web/src/api/usersApi.ts` (37 lines)

## Pattern File

`frontend/taskdeck-web/src/tests/api/archiveApi.spec.ts`

## Test File to Create

`frontend/taskdeck-web/src/tests/api/usersApi.spec.ts`

## Test Cases

Read the source file and create one test per exported method. Each test should:
- Mock `http` with `vi.mock`
- Call the method with sample arguments
- Assert the correct HTTP method, URL, and payload were used

## Verify

```bash
cd frontend/taskdeck-web && npx vitest --run -t "usersApi"
```

## Acceptance Criteria

- All exported methods covered
- No lint warnings
- Commit on branch, push, open PR linking the GitHub issue
