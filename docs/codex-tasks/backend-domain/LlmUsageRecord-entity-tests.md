# Task: Add LlmUsageRecord entity unit tests

- **GitHub Issue**: [#426](https://github.com/Chris0Jeky/Taskdeck/issues/426) (TST-CODEX-12)
- **Branch**: `test/llm-usage-record-entity-tests`
- **Priority**: Tier 4 (easy)

## Source File

`backend/src/Taskdeck.Domain/Entities/LlmUsageRecord.cs`

## Pattern File

Find an existing entity test in `backend/tests/Taskdeck.Domain.Tests/Entities/` and follow that exact pattern.

## Test File to Create

`backend/tests/Taskdeck.Domain.Tests/Entities/LlmUsageRecordTests.cs`

## Test Cases

1. Construction sets all required properties (provider, token counts, timestamps)
2. Default values are correct
3. Any computed properties (e.g., total cost) calculate correctly
4. Edge cases: zero tokens, null provider, etc.

## Verify

```bash
dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~LlmUsageRecordTests"
```

## Acceptance Criteria

- All tests pass, 0 errors, 0 warnings
- Commit on branch, push, open PR linking the GitHub issue
