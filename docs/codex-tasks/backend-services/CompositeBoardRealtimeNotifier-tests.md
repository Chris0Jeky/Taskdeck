# Task: Add CompositeBoardRealtimeNotifier unit tests

- **GitHub Issue**: [#429](https://github.com/Chris0Jeky/Taskdeck/issues/429) (TST-CODEX-15)
- **Branch**: `test/composite-realtime-notifier-tests`
- **Priority**: Tier 5 (medium)

## Source File

Find `CompositeBoardRealtimeNotifier.cs` in `backend/src/` (likely in Infrastructure/).

## Pattern File

Find existing notifier or service tests in `backend/tests/` and follow the same pattern.

## Test File to Create

Place in the appropriate test project matching the source file's layer.

## Test Cases

1. Notifying delegates to all registered inner notifiers
2. If one inner notifier throws, the others still get called (fault isolation)
3. Empty notifier list does not throw
4. All notification event types are forwarded correctly (read the interface to find which events exist)
5. Notification arguments are passed through unmodified to each inner notifier

## Implementation Notes

- Create mock/stub implementations of the inner notifier interface
- Use xUnit + Moq or NSubstitute (check which mocking library the project already uses by looking at existing test `.csproj` PackageReferences)
- Focus on verifying delegation and fault isolation

## Verify

```bash
dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~CompositeBoardRealtimeNotifier"
```

## Acceptance Criteria

- All tests pass, 0 errors, 0 warnings
- Commit on branch, push, open PR linking the GitHub issue
