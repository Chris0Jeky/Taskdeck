# Task: Add CardComment entity unit tests

- **GitHub Issue**: [#423](https://github.com/Chris0Jeky/Taskdeck/issues/423) (TST-CODEX-09)
- **Branch**: `test/cardcomment-entity-tests`
- **Priority**: Tier 4 (easy)

## Source File

`backend/src/Taskdeck.Domain/Entities/CardComment.cs`

## Pattern File

Find an existing entity test in `backend/tests/Taskdeck.Domain.Tests/Entities/` and follow that exact pattern (xUnit, FluentAssertions or Assert).

## Test File to Create

`backend/tests/Taskdeck.Domain.Tests/Entities/CardCommentTests.cs`

## Test Cases

Read the entity source, then test:

1. Construction with valid parameters sets all properties correctly
2. Default values (Id, CreatedAt, etc.) are initialized as expected
3. Any validation logic or guard clauses throw on invalid input
4. Property setters work correctly (if any have side effects)
5. Any domain methods on the entity behave as documented

## Implementation Notes

- Use xUnit `[Fact]` or `[Theory]` attributes
- Namespace: `Taskdeck.Domain.Tests.Entities`
- The `.csproj` is at `backend/tests/Taskdeck.Domain.Tests/Taskdeck.Domain.Tests.csproj`

## Verify

```bash
dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~CardCommentTests"
```

## Acceptance Criteria

- All tests pass
- Build has 0 errors, 0 warnings
- Commit on branch, push, open PR linking the GitHub issue
