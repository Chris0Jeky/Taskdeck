# Task: Add Notification entity unit tests

- **GitHub Issue**: [#424](https://github.com/Chris0Jeky/Taskdeck/issues/424) (TST-CODEX-10)
- **Branch**: `test/notification-entity-tests`
- **Priority**: Tier 4 (easy)

## Source File

`backend/src/Taskdeck.Domain/Entities/Notification.cs`

## Pattern File

Find an existing entity test in `backend/tests/Taskdeck.Domain.Tests/Entities/` and follow that exact pattern.

## Test File to Create

`backend/tests/Taskdeck.Domain.Tests/Entities/NotificationTests.cs`

## Test Cases

1. Construction with valid parameters sets all properties correctly
2. Default values are initialized as expected
3. Any validation logic throws on invalid input
4. Read/unread state transitions work correctly (if the entity has such methods)
5. Any domain methods behave correctly

## Verify

```bash
dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~NotificationTests"
```

## Acceptance Criteria

- All tests pass, 0 errors, 0 warnings
- Commit on branch, push, open PR linking the GitHub issue
