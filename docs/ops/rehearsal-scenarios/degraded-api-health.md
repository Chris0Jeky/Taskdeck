# Scenario: Degraded API Health

Last Updated: 2026-03-29
Issue: `#150` OPS-19 incident rehearsal and recovery evidence program

## Overview

Simulate a condition where the `/health/ready` endpoint returns `503 NotReady` due to a degraded subsystem (database unreachable, queue backlog exceeded, or worker heartbeat stale). Diagnose which check failed and recover the system to a healthy state.

## Pre-Conditions

- Repository checked out at a known commit on `main`.
- Backend builds successfully: `dotnet build backend/Taskdeck.sln -c Release`
- No other Taskdeck API instance running on port 5000.
- SQLite database file is accessible (default: `taskdeck.db` in the API project directory).
- `curl` or equivalent HTTP client available.

## Injection Method

Choose one of the following fault injection approaches:

### Option A: Database Connectivity Fault

Rename or lock the SQLite database file before starting the API so the database connectivity check fails.

```bash
# From repo root
cd backend/src/Taskdeck.Api
# Rename the DB file to simulate missing database
mv taskdeck.db taskdeck.db.bak 2>/dev/null || true
# Start the API
dotnet run --project Taskdeck.Api.csproj
```

Note: EF Core with SQLite will auto-create a new empty database. To truly break connectivity, set the connection string to a read-only or non-existent directory:

```bash
ConnectionStrings__DefaultConnection="Data Source=/nonexistent/path/taskdeck.db" \
  dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj
```

### Option B: Worker Heartbeat Staleness

Start the API with queue processing enabled, then observe the heartbeat staleness window. The `queueToProposal` worker is considered stale if its last heartbeat exceeds `QueuePollIntervalSeconds * 3` (minimum 30 seconds). The `proposalHousekeeping` worker goes stale after 3 minutes.

To inject staleness without code changes: start the API, wait for workers to begin heartbeating, then suspend the worker thread (not practical without code changes). Instead, inspect the staleness values reported by `/health/ready` and understand the thresholds.

For a realistic rehearsal: modify `appsettings.Development.json` temporarily to set `Workers:QueuePollIntervalSeconds` to 1, then observe how quickly the worker goes stale if delayed.

### Option C: Queue Backlog Overload

Flood the LLM queue with pending items to exceed the threshold (`MaxBatchSize * 20`, minimum 100):

```bash
# Start the API
dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj &

# Register and authenticate
curl -s -X POST http://localhost:5000/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"username":"rehearsal-user","password":"Rehearsal123!"}'

TOKEN=$(curl -s -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"rehearsal-user","password":"Rehearsal123!"}' | jq -r '.token')

# Create a board to target
BOARD_ID=$(curl -s -X POST http://localhost:5000/api/boards \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"name":"rehearsal-board"}' | jq -r '.id')

# Submit many LLM queue items (adjust count to exceed threshold)
for i in $(seq 1 120); do
  curl -s -X POST http://localhost:5000/api/llm-queue \
    -H "Content-Type: application/json" \
    -H "Authorization: Bearer $TOKEN" \
    -d "{\"boardId\":\"$BOARD_ID\",\"requestType\":\"Suggest\",\"payload\":\"item $i\"}" > /dev/null
done
```

## Expected Diagnosis Path

1. **Check health endpoints**:
   ```bash
   curl -s http://localhost:5000/health/live | jq .
   curl -s http://localhost:5000/health/ready | jq .
   ```

2. **Interpret the response**: The `/health/ready` response includes a `checks` object with `database`, `queue`, and `workers` sub-checks. Each has a `status` field (`Healthy`, `Degraded`, `Unhealthy`, `Stale`, `Starting`, `Disabled`).

3. **Identify the failing check**: Look for non-`Healthy` status values. Examples:
   - `checks.database.status: "Unhealthy"` with an `error` field
   - `checks.queue.status: "Degraded"` with `depth` exceeding `threshold`
   - `checks.workers.queueToProposal.status: "Stale"` with `stalenessSeconds` exceeding `maxStalenessSeconds`

4. **Correlate with logs**: Check the API console output for error messages related to the failing subsystem.

5. **Verify with telemetry** (if OpenTelemetry is enabled): Check for `taskdeck.automation.queue.backlog` and `taskdeck.worker.heartbeat.staleness` metrics.

## Recovery Steps

### Database Fault Recovery

```bash
# Restore the original database
mv taskdeck.db.bak taskdeck.db
# Or fix the connection string and restart
# Verify recovery
curl -s http://localhost:5000/health/ready | jq .checks.database
```

### Queue Backlog Recovery

The queue will drain naturally as the worker processes items. To accelerate:

```bash
# Check current queue depth
curl -s http://localhost:5000/health/ready | jq .checks.queue

# Wait for worker to process items, or restart with higher batch size
# Workers__MaxBatchSize=20 dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj
```

### Worker Staleness Recovery

```bash
# If the worker is stuck, restart the API process
# Verify heartbeats resume
curl -s http://localhost:5000/health/ready | jq .checks.workers
```

## Evidence Checklist

After completing the rehearsal, verify the evidence package includes:

- [ ] Screenshot or captured output of the degraded `/health/ready` response (503)
- [ ] Identification of which specific check failed (database, queue, or workers)
- [ ] The exact `status`, `error`, or threshold values from the response
- [ ] Commands used to inject the fault
- [ ] Commands used to diagnose the fault
- [ ] Commands used to recover
- [ ] Captured output of the recovered `/health/ready` response (200)
- [ ] Any log excerpts showing error or warning messages during the degraded state
- [ ] Findings about gaps in the health response (e.g., missing context, unclear error messages)

## Related Documents

- `backend/src/Taskdeck.Api/Controllers/HealthController.cs` -- health endpoint implementation
- `docs/ops/OBSERVABILITY_BASELINE.md` -- telemetry contract
- `docs/ops/FAILURE_INJECTION_DRILLS.md` -- automated drill scripts
