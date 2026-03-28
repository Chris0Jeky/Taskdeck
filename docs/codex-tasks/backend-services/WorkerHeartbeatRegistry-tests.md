# Task: Add WorkerHeartbeatRegistry unit tests

- **GitHub Issue**: [#428](https://github.com/Chris0Jeky/Taskdeck/issues/428) (TST-CODEX-14)
- **Branch**: `test/worker-heartbeat-registry-tests`
- **Priority**: Tier 5 (medium)

## Source File

Find `WorkerHeartbeatRegistry.cs` in `backend/src/` (likely in Infrastructure/Workers/ or Application/Workers/).

## Pattern File

Find existing worker tests in `backend/tests/` (search for `Worker` in test file names) and follow the same pattern.

## Test File to Create

Place in the appropriate test project matching the source file's layer (likely `Taskdeck.Application.Tests` or `Taskdeck.Infrastructure.Tests`).

## Test Cases

1. Registering a worker adds it to the registry
2. Heartbeat update refreshes the worker's last-seen timestamp
3. Querying for stale workers returns workers past the timeout threshold
4. Removing a worker removes it from the registry
5. Registering the same worker twice does not create duplicates
6. Empty registry returns no stale workers

## Implementation Notes

- This is likely a simple in-memory registry — no external dependencies to mock
- May need to manipulate time (use `ISystemClock` or similar if available, otherwise test with short timeouts)

## Verify

```bash
dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~WorkerHeartbeatRegistry"
```

## Acceptance Criteria

- All tests pass, 0 errors, 0 warnings
- Commit on branch, push, open PR linking the GitHub issue
