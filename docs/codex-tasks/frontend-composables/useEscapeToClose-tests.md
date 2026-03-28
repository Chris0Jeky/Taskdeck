# Task: Add useEscapeToClose unit tests

- **GitHub Issue**: [#419](https://github.com/Chris0Jeky/Taskdeck/issues/419) (TST-CODEX-05)
- **Branch**: `test/useEscapeToClose-unit-tests`
- **Priority**: Tier 2 (easy)

## Source File

`frontend/taskdeck-web/src/composables/useEscapeToClose.ts` (19 lines)

## Pattern File

`frontend/taskdeck-web/src/tests/composables/useEscapeStack.spec.ts`

## Test File to Create

`frontend/taskdeck-web/src/tests/composables/useEscapeToClose.spec.ts`

## Test Cases

1. Pressing Escape key calls the provided close callback
2. Non-Escape keys do not trigger the callback
3. Cleanup (unmount) removes the event listener — verify callback is not called after cleanup

## Implementation Notes

- Use `document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))` to simulate
- May need to mount in a Vue test wrapper if the composable uses lifecycle hooks

## Verify

```bash
cd frontend/taskdeck-web && npx vitest --run -t "useEscapeToClose"
```

## Acceptance Criteria

- All 3 test cases pass
- No lint warnings
- Commit on branch, push, open PR linking the GitHub issue
