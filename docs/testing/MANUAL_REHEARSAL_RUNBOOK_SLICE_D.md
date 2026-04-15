# Manual Rehearsal Runbook -- Slice D: Ops CLI, Log Query, and Health Telemetry

Last Updated: 2026-04-15

Companion references:
- `docs/testing/MANUAL_VALIDATION_SLICE_D_SCENARIOS.md` (detailed scenario catalog)
- `docs/MANUAL_TEST_CHECKLIST.md` (parent checklist, Section F)
- `docs/STATUS.md`

## Purpose

Step-by-step operator instructions for rehearsing Slice D validation. This runbook is designed for an operator who may not be familiar with the codebase -- every command and expected result is explicit. Capture evidence at each checkpoint.

## Pre-Rehearsal Setup

### 1. Environment Preparation

```bash
# Stop any running API/frontend processes
# Remove stale database for a clean baseline
rm -f backend/src/Taskdeck.Api/taskdeck.db

# Start backend
dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj &

# Start frontend
cd frontend/taskdeck-web
npm run dev &
```

Wait for both servers to report ready (API on `:5000`, frontend on `:5173` or fallback port).

### 2. Record Run Metadata

Fill this in before starting:

| Field | Value |
|---|---|
| Date/time (UTC) | |
| Commit SHA | `git rev-parse HEAD` |
| Browser and version | |
| OS | |
| DB baseline | `fresh` |
| Env flags changed | none |

### 3. Register Test User

Open `http://localhost:5173/register` and create a test account:
- Username: `ops-rehearsal-user`
- Email: `ops-rehearsal@test.local`
- Password: `TestPass1!`

After registration, verify you land on `/workspace/home`.

---

## Rehearsal Steps

### Phase 1: Health Endpoints (API-Level Validation)

These checks use curl or the browser directly -- no UI needed yet.

**Step 1.1: Liveness probe**

```bash
curl -s http://localhost:5000/health/live | python -m json.tool
```

Checkpoint:
- [ ] Response status: 200
- [ ] Body contains `"status": "Healthy"`
- [ ] Body contains `"timestamp"` field

**Step 1.2: Readiness probe**

```bash
curl -s http://localhost:5000/health/ready | python -m json.tool
```

Checkpoint:
- [ ] Response status: 200 (or 503 if called within 30s of startup)
- [ ] `checks.database.status` is `"Healthy"`
- [ ] `checks.queue` contains `depth`, `totalDepth`, `captureDepth`, `threshold`
- [ ] `checks.queue.depth` is 0 (no automation requests on fresh DB)
- [ ] `checks.signalrBackplane.status` is `"NotConfigured"` (local dev, no Redis)
- [ ] `checks.workers.queueToProposal` has `status`, `lastHeartbeat`, `stalenessSeconds`, `maxStalenessSeconds`
- [ ] `checks.workers.proposalHousekeeping` has same structure

Evidence: Save response body as `evidence/health-ready-baseline.json`.

**Step 1.3: Readiness probe -- worker heartbeat convergence**

If you called `/health/ready` within 30 seconds of API startup and saw `"Starting"` for workers, wait 30 seconds and repeat:

```bash
sleep 35
curl -s http://localhost:5000/health/ready | python -m json.tool
```

Checkpoint:
- [ ] Worker statuses have transitioned from `"Starting"` to `"Healthy"`
- [ ] `lastHeartbeat` fields are non-null
- [ ] `stalenessSeconds` is a small number (< 30)

Evidence: Save response body as `evidence/health-ready-converged.json`.

---

### Phase 2: Ops Console -- CLI Runner

**Step 2.1: Navigate to ops console**

Open `http://localhost:5173/workspace/ops/cli` in the browser.

Checkpoint:
- [ ] Page heading says "Ops Console"
- [ ] Template selector is visible with at least `health.check`
- [ ] Role context panel shows current role and runnable templates
- [ ] Three tabs visible: CLI Runner, Endpoint Explorer, Logs

Evidence: Screenshot `evidence/ops-console-loaded.png`.

**Step 2.2: Execute health.check**

1. Select `health.check` in the template selector.
2. Leave parameters as `{}`.
3. Click "Run Template".

Checkpoint:
- [ ] Output area shows `> health.check`
- [ ] Run status shows `Completed`
- [ ] Output contains `Health check: OK`
- [ ] "Last run ID" appears below output with a GUID

Evidence: Screenshot `evidence/health-check-output.png`. Record the run ID: `__________`.

**Step 2.3: Invalid parameters**

1. Keep `health.check` selected.
2. Change parameters to `{"unexpected": "value"}`.
3. Click "Run Template".

Checkpoint:
- [ ] Error message appears in output or toast
- [ ] Error references validation failure

Evidence: Screenshot `evidence/invalid-params-error.png`.

**Step 2.4: Restricted template (if available)**

1. If a restricted template appears (e.g., `boards.list` requiring admin), select it.
2. Note the "restricted for your role" warning.
3. Click "Run Template".

Checkpoint:
- [ ] 403 response with guidance message
- [ ] Message mentions required role, current role, and path to Settings

Evidence: Screenshot `evidence/restricted-template.png`.

---

### Phase 3: Ops Console -- Logs Tab

**Step 3.1: Broad log query**

1. Click the "Logs" tab.
2. Set level to "All levels", source to "all", correlation ID blank.
3. Click "Refresh".

Checkpoint:
- [ ] Log entries appear (at least entries from the health.check run)
- [ ] Each entry shows timestamp, level, source, message
- [ ] At least one entry has source `OpsCliService`

Evidence: Screenshot `evidence/logs-broad-query.png`.

**Step 3.2: Level filter**

1. Set level filter to "Info".
2. Click "Refresh".

Checkpoint:
- [ ] Only Info-level entries are shown

3. Set level filter to "Error".
4. Click "Refresh".

Checkpoint:
- [ ] Only Error-level entries shown (may be empty -- that is normal on a fresh run)

Evidence: Screenshots of each filter state.

**Step 3.3: Source filter**

1. Type `OpsCliService` in the source filter.
2. Click "Refresh".

Checkpoint:
- [ ] Only entries with source `OpsCliService` appear

3. Type `NonexistentSource` in the source filter.
4. Click "Refresh".

Checkpoint:
- [ ] Empty state: "No logs match the current filters"
- [ ] "Clear Filters" button visible

Evidence: Screenshots `evidence/logs-source-filter.png` and `evidence/logs-empty-state.png`.

**Step 3.4: Correlation ID lookup**

1. Enter the correlation ID from Step 2.2 (the run ID or correlation ID from the health.check output).
2. Click "Refresh".

Checkpoint:
- [ ] Entries filtered to only those matching the correlation ID
- [ ] All entries share the same correlation ID in the rightmost column

Evidence: Screenshot `evidence/logs-correlation-filter.png`.

**Step 3.5: Nonexistent correlation ID**

1. Enter `00000000000000000000000000000000` as correlation ID.
2. Click "Refresh".

Checkpoint:
- [ ] Empty state: "No logs for this correlation ID"
- [ ] Guidance text suggests checking the ID or clearing the filter

Evidence: Screenshot `evidence/logs-correlation-notfound.png`.

**Step 3.6: Clear filters**

1. Click "Clear Filters" button.

Checkpoint:
- [ ] All filters reset
- [ ] Entries reappear

---

### Phase 4: Correlation ID Trail Verification

**Step 4.1: End-to-end trace**

1. Open browser DevTools (F12) > Network tab.
2. Go to CLI Runner tab and run `health.check`.
3. Find the `POST /api/ops/cli/run` request in the network log.
4. Inspect the response body -- note the `correlationId` field.
5. Check the response headers for `X-Request-Id`.

Checkpoint:
- [ ] `correlationId` is present in response body
- [ ] `X-Request-Id` header matches the `correlationId`

6. Switch to Logs tab, paste the correlation ID, and refresh.

Checkpoint:
- [ ] Entries appear matching the correlation
- [ ] The trace is coherent: entries show command lifecycle events

Evidence: Screenshot of network response + matching log entries.

**Step 4.2: Cross-user isolation (requires two user accounts)**

Using curl or a second browser in incognito:

```bash
# Register User B
curl -s -X POST http://localhost:5000/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"username":"ops-user-b","email":"ops-b@test.local","password":"TestPass2!"}' \
  | python -m json.tool
# Save token as TOKEN_B

# Try to access User A's correlation ID
curl -s -H "Authorization: Bearer $TOKEN_B" \
  "http://localhost:5000/api/logs/correlation/<USER_A_CORRELATION_ID>" \
  | python -m json.tool
```

Checkpoint:
- [ ] Response status: 403 Forbidden
- [ ] Error message indicates permission denied for this correlation

Evidence: curl output saved as `evidence/cross-user-isolation.txt`.

---

### Phase 5: Endpoint Explorer

**Step 5.1: Basic GET request**

1. Click "Endpoint Explorer" tab.
2. Method: GET, Path: `/boards`.
3. Click "Send".

Checkpoint:
- [ ] Response panel appears
- [ ] Status code 200 displayed in green
- [ ] Response body is valid JSON (board list)

**Step 5.2: Health endpoint probing**

1. Method: GET, Path: `/health/ready` (note: the explorer may or may not strip `/api` prefix -- try both `/health/ready` and `http://localhost:5000/health/ready`).
2. Click "Send".

Checkpoint:
- [ ] Health response visible in the response panel

Evidence: Screenshot `evidence/endpoint-explorer.png`.

---

### Phase 6: Tab Navigation

**Step 6.1: URL sync**

1. Verify URL is `/workspace/ops/cli` when CLI Runner tab is active.
2. Click "Endpoint Explorer" -- verify URL changes to `/workspace/ops/endpoints`.
3. Click "Logs" -- verify URL changes to `/workspace/ops/logs`.
4. Enter `/workspace/ops/logs` directly in the address bar and press Enter.
5. Verify Logs tab is active.

Checkpoint:
- [ ] Tab state and URL stay synchronized in both directions

---

## Post-Rehearsal

### Evidence Package

Collect all files from `evidence/` directory:
- `health-ready-baseline.json`
- `health-ready-converged.json`
- `ops-console-loaded.png`
- `health-check-output.png`
- `invalid-params-error.png`
- `restricted-template.png`
- `logs-broad-query.png`
- `logs-source-filter.png`
- `logs-empty-state.png`
- `logs-correlation-filter.png`
- `logs-correlation-notfound.png`
- `cross-user-isolation.txt`
- `endpoint-explorer.png`

### Summary Checklist

| Phase | Status | Notes |
|---|---|---|
| Phase 1: Health Endpoints | Pass / Fail | |
| Phase 2: Ops CLI Runner | Pass / Fail | |
| Phase 3: Logs Tab | Pass / Fail | |
| Phase 4: Correlation Trail | Pass / Fail | |
| Phase 5: Endpoint Explorer | Pass / Fail | |
| Phase 6: Tab Navigation | Pass / Fail | |

### Final Run Metadata

| Field | Value |
|---|---|
| Completion time (UTC) | |
| Total duration | |
| Findings/issues | |
| Artifacts location | |
