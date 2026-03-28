# Task: Add AutomationProposal entity unit tests

- **GitHub Issue**: [#425](https://github.com/Chris0Jeky/Taskdeck/issues/425) (TST-CODEX-11)
- **Branch**: `test/automation-proposal-entity-tests`
- **Priority**: Tier 4 (easy)

## Source File

`backend/src/Taskdeck.Domain/Entities/AutomationProposal.cs`

## Pattern File

Find an existing entity test in `backend/tests/Taskdeck.Domain.Tests/Entities/` and follow that exact pattern.

## Test File to Create

`backend/tests/Taskdeck.Domain.Tests/Entities/AutomationProposalTests.cs`

## Test Cases

1. Construction sets all required properties
2. Status/lifecycle state defaults are correct
3. Any status transition methods (approve, reject, execute) work correctly
4. Invalid state transitions throw or return error
5. Edge cases: null/empty fields where validation exists

## Verify

```bash
dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~AutomationProposalTests"
```

## Acceptance Criteria

- All tests pass, 0 errors, 0 warnings
- Commit on branch, push, open PR linking the GitHub issue
