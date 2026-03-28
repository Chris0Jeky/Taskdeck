# Task: Add labelsApi unit tests

- **GitHub Issue**: [#415](https://github.com/Chris0Jeky/Taskdeck/issues/415) (TST-CODEX-01)
- **Branch**: `test/labelsApi-unit-tests`
- **Priority**: Tier 1 (trivial)

## Source File

`frontend/taskdeck-web/src/api/labelsApi.ts` (23 lines, 4 methods)

## Pattern File

`frontend/taskdeck-web/src/tests/api/archiveApi.spec.ts` — follow this exact structure.

## Test File to Create

`frontend/taskdeck-web/src/tests/api/labelsApi.spec.ts`

## Test Cases

1. `getLabels` — calls `http.get` with `/boards/{boardId}/labels`
2. `createLabel` — calls `http.post` with `/boards/{boardId}/labels` and forwards the label DTO
3. `updateLabel` — calls `http.patch` with `/boards/{boardId}/labels/{labelId}` and forwards the update DTO
4. `deleteLabel` — calls `http.delete` with `/boards/{boardId}/labels/{labelId}`

## Implementation Notes

- Mock `../../api/http` with `vi.mock` (get, post, patch, delete)
- Use `vi.mocked(http.get).mockResolvedValue({ data: ... })` pattern
- Verify URL string interpolation with sample boardId and labelId values
- Verify payload is forwarded for create/update

## Verify

```bash
cd frontend/taskdeck-web && npx vitest --run -t "labelsApi"
```

## Acceptance Criteria

- All 4 test cases pass
- No lint warnings: `npm run lint`
- Commit on branch, push, open PR linking the GitHub issue
