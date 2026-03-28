# Task: Add useShortcutContext unit tests

- **GitHub Issue**: [#420](https://github.com/Chris0Jeky/Taskdeck/issues/420) (TST-CODEX-06)
- **Branch**: `test/useShortcutContext-unit-tests`
- **Priority**: Tier 2 (medium)

## Source File

`frontend/taskdeck-web/src/composables/useShortcutContext.ts` (109 lines)

## Pattern File

`frontend/taskdeck-web/src/tests/composables/useKeyboardShortcuts.spec.ts`

## Test File to Create

`frontend/taskdeck-web/src/tests/composables/useShortcutContext.spec.ts`

## Test Cases

Read the source file carefully to understand the context stack mechanism, then test:

1. Pushing a context makes it the active context
2. Popping a context restores the previous one
3. Shortcuts registered in the active context fire when triggered
4. Shortcuts in a non-active (stacked) context do NOT fire
5. After popping, the restored context's shortcuts fire again
6. Multiple contexts can be stacked and unwound correctly
7. Edge case: popping an empty stack is safe (no error)

## Implementation Notes

- This composable likely manages a stack of keyboard shortcut contexts
- Read the exported functions and types before writing tests
- May need to simulate keyboard events via `document.dispatchEvent`

## Verify

```bash
cd frontend/taskdeck-web && npx vitest --run -t "useShortcutContext"
```

## Acceptance Criteria

- All test cases pass
- No lint warnings
- Commit on branch, push, open PR linking the GitHub issue
