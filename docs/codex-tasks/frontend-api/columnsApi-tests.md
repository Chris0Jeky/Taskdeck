# Task: Add columnsApi unit tests

- **GitHub Issue**: [#416](https://github.com/Chris0Jeky/Taskdeck/issues/416) (TST-CODEX-02)
- **Branch**: `test/columnsApi-unit-tests`
- **Priority**: Tier 1 (trivial)

## Source File

`frontend/taskdeck-web/src/api/columnsApi.ts` (30 lines, 5 methods)

## Pattern File

`frontend/taskdeck-web/src/tests/api/archiveApi.spec.ts`

## Test File to Create

`frontend/taskdeck-web/src/tests/api/columnsApi.spec.ts`

## Test Cases

1. `getColumns` — calls `http.get` with `/boards/{boardId}/columns`
2. `createColumn` — calls `http.post` with `/boards/{boardId}/columns` and forwards DTO
3. `updateColumn` — calls `http.patch` with `/boards/{boardId}/columns/{columnId}` and forwards DTO
4. `deleteColumn` — calls `http.delete` with `/boards/{boardId}/columns/{columnId}`
5. `reorderColumns` — calls `http.post` with `/boards/{boardId}/columns/reorder` and `{ columnIds }` payload

## Verify

```bash
cd frontend/taskdeck-web && npx vitest --run -t "columnsApi"
```

## Acceptance Criteria

- All 5 test cases pass
- No lint warnings
- Commit on branch, push, open PR linking the GitHub issue
