# Rehearsal Evidence: Degraded API Health

## Metadata

| Field | Value |
| --- | --- |
| Date | 2026-03-29 |
| Rehearsal type | Monthly (initial program setup) |
| Scenario | `docs/ops/rehearsal-scenarios/degraded-api-health.md` |
| Lead | @Chris0Jeky |
| Participants | @Chris0Jeky |
| Commit SHA | `440a8c9dbfe63a9d631437ca97069ea34e51c81e` |
| OS / Environment | Windows 10 Pro 10.0.19045, .NET 8, SQLite |
| Duration | ~15 minutes |
| Outcome | Partial |

## Timeline

| Timestamp (UTC) | Actor | Action / Observation |
| --- | --- | --- |
| 2026-03-29T03:27:00Z | @Chris0Jeky | Verified backend builds successfully (`dotnet build -c Release`, 0 warnings, 0 errors) |
| 2026-03-29T03:27:30Z | @Chris0Jeky | Ran `HealthApiTests` -- 3/3 passing (Live, Ready, CaptureBacklogExclusion) |
| 2026-03-29T03:28:08Z | @Chris0Jeky | Started API on port 5099, confirmed healthy baseline |
| 2026-03-29T03:28:11Z | @Chris0Jeky | Captured `/health/live` response: `{"status":"Healthy"}` |
| 2026-03-29T03:28:11Z | @Chris0Jeky | Captured `/health/ready` response: HTTP 200, all checks Healthy |
| 2026-03-29T03:28:12Z | @Chris0Jeky | Stopped API, attempted injection Option A (invalid DB path) |
| 2026-03-29T03:28:29Z | @Chris0Jeky | Started API with `ConnectionStrings__DefaultConnection="Data Source=/nonexistent/path/taskdeck.db"` |
| 2026-03-29T03:28:29Z | @Chris0Jeky | **Finding**: API started healthy -- SQLite auto-created database at mapped path. Invalid Unix-style path was silently resolved on Windows |
| 2026-03-29T03:28:40Z | @Chris0Jeky | Attempted injection via `Workers__EnableAutoQueueProcessing=false` to disable queue worker |
| 2026-03-29T03:28:42Z | @Chris0Jeky | API started, all checks showed Healthy. Queue worker still heartbeating (launchSettings.json may override env vars) |
| 2026-03-29T03:29:17Z | @Chris0Jeky | After 35s wait (past startup grace), all workers still Healthy. `proposalHousekeeping` staleness=8s (max=180s), `queueToProposal` staleness=2.9s (max=30s) |
| 2026-03-29T03:29:20Z | @Chris0Jeky | Concluded rehearsal. Documented findings |

## Commands Run

```bash
# Build verification
dotnet build backend/Taskdeck.sln -c Release

# Run health tests
dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~HealthApiTests"

# Healthy baseline
dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj -- --urls "http://localhost:5099"
curl -s http://localhost:5099/health/live
curl -s http://localhost:5099/health/ready

# Injection attempt A: invalid DB path
ConnectionStrings__DefaultConnection="Data Source=/nonexistent/path/taskdeck.db" \
  dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj -- --urls "http://localhost:5099"
curl -s http://localhost:5099/health/ready

# Injection attempt B: disable queue processing
Workers__EnableAutoQueueProcessing=false \
  dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj -- --urls "http://localhost:5099"
curl -s http://localhost:5099/health/ready
# Waited 35s for startup grace to expire, checked again
curl -s http://localhost:5099/health/ready
```

## Log Excerpts

Healthy baseline `/health/ready` response:
```json
{
  "status": "Ready",
  "timestamp": "2026-03-29T03:28:11.466Z",
  "checks": {
    "database": { "status": "Healthy" },
    "queue": { "status": "Healthy", "depth": 0, "totalDepth": 0, "captureDepth": 0, "threshold": 100 },
    "workers": {
      "queueToProposal": { "status": "Healthy", "lastHeartbeat": "2026-03-29T03:28:09.177Z", "stalenessSeconds": 2.28, "maxStalenessSeconds": 30 },
      "proposalHousekeeping": { "status": "Healthy", "lastHeartbeat": "2026-03-29T03:28:09.506Z", "stalenessSeconds": 1.96, "maxStalenessSeconds": 180 }
    }
  }
}
```

Invalid DB path response (still healthy):
```json
{
  "status": "Ready",
  "timestamp": "2026-03-29T03:28:29.821Z",
  "checks": {
    "database": { "status": "Healthy" },
    "queue": { "status": "Healthy", "depth": 0, "totalDepth": 0, "captureDepth": 0, "threshold": 100 },
    "workers": {
      "queueToProposal": { "status": "Healthy", "lastHeartbeat": "2026-03-29T03:28:29.586Z", "stalenessSeconds": 0.25, "maxStalenessSeconds": 30 },
      "proposalHousekeeping": { "status": "Healthy", "lastHeartbeat": "2026-03-29T03:28:09.506Z", "stalenessSeconds": 20.31, "maxStalenessSeconds": 180 }
    }
  }
}
```

## Root Cause / Diagnosis Summary

The rehearsal targeted Option A (database connectivity fault) and Option B-adjacent (worker heartbeat staleness). Two key observations:

1. **SQLite auto-creation resilience**: Setting `ConnectionStrings__DefaultConnection` to a non-existent Unix-style path (`/nonexistent/path/taskdeck.db`) did not degrade the health endpoint. On Windows, the path was silently resolved (likely to a relative path), and SQLite's `CanConnectAsync` succeeded because the provider auto-creates the database file. This means the database check is resilient to missing-DB scenarios but may mask genuine connection string misconfiguration.

2. **Environment variable override vs launchSettings**: The `Workers__EnableAutoQueueProcessing=false` environment variable did not visibly change worker behavior when `launchSettings.json` was in use (via `dotnet run`). The `--no-launch-profile` flag would be needed to ensure environment variables take precedence.

## Recovery Actions Taken

No recovery was needed -- the system never entered a degraded state due to the resilience of the SQLite provider and the launchSettings override behavior.

## Findings

- [ ] Finding 1: SQLite `CanConnectAsync` succeeds even with a non-existent file path because the provider auto-creates the database. The health check's database connectivity test does not distinguish between "connected to the intended database" and "connected to an auto-created empty database." -- Severity: P3 -- Issue: to be filed
- [ ] Finding 2: Environment variable overrides for worker settings are ineffective when using `dotnet run` with `launchSettings.json`. The scenario documentation should specify `--no-launch-profile` for reliable fault injection via environment variables. -- Severity: P3 -- Issue: documented in scenario update
- [ ] Finding 3: The degraded-api-health scenario's Option A (invalid DB path) is not reliably reproducible on Windows due to Unix-to-Windows path resolution. The scenario should include Windows-specific injection guidance. -- Severity: P4 -- Issue: documented in scenario update

## Sign-Off

| Role | Name | Date | Approved |
| --- | --- | --- | --- |
| Rehearsal lead | @Chris0Jeky | 2026-03-29 | [x] |

## Follow-Up Issues

- Scenario documentation updated with findings (same PR)
- P3 finding about SQLite auto-creation masking connection errors should be tracked in a future hardening issue
