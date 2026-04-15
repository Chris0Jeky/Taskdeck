# Manual Validation Slice D: Ops CLI, Log Query/Correlation, and Health Telemetry Behavior

Last Updated: 2026-04-15

Companion references:
- `docs/MANUAL_TEST_CHECKLIST.md` (parent checklist, Section F)
- `docs/STATUS.md` (current implementation snapshot)
- `docs/TESTING_GUIDE.md` (test operations reference)
- `docs/testing/manual-validation-a-workspace-board-ux.md` (Slice A)
- `docs/testing/manual-validation-b-authz-contracts.md` (Slice B)

## Purpose

Validate the operator-facing surfaces: ops CLI template execution, log querying with filter combinations, correlation ID propagation from request through command run to log entries, and health/readiness endpoint contracts including worker heartbeat and queue depth telemetry.

This slice covers Section F of the parent checklist and extends it with edge cases, correlation trail verification, and the operator troubleshooting journey.

## Environment Setup

### Prerequisites

1. Clean backend database (remove `backend/src/Taskdeck.Api/taskdeck.db` if present).
2. Start backend:
   ```bash
   dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj
   ```
3. Start frontend:
   ```bash
   cd frontend/taskdeck-web
   npm run dev
   ```
4. Open `http://localhost:5173` (or fallback port printed by Vite).
5. Register a test user and log in.
6. Verify the ops feature flag is enabled (navigate to `/workspace/ops/cli` -- it should load without redirect).

### Run Metadata (Record Before and After Each Run)

| Field | Value |
|---|---|
| Date/time (UTC) | |
| Commit SHA | |
| Browser and version | |
| OS | |
| DB baseline | `fresh` / `existing` |
| Env flags changed | |
| Artifacts collected | |

---

## TST10-SC-001: Ops Console Page Load

**Goal:** Verify the ops console renders correctly with templates loaded.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Navigate to `/workspace/ops/cli` | Page renders with heading "Ops Console" |
| 2 | Observe template selector | Templates load into the combobox (at minimum `health.check`) |
| 3 | Observe role context panel | Current role label is displayed; runnable templates are listed |
| 4 | Observe tab bar | Three tabs visible: "CLI Runner", "Endpoint Explorer", "Logs" |

**Evidence:** Screenshot of ops console with templates loaded.

---

## TST10-SC-002: Execute health.check Template

**Goal:** Verify the `health.check` template executes successfully and produces output.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Select `health.check` in the template selector | Template meta shows description, role, timeout, and parameters |
| 2 | Leave parameters as default (`{}`) | No validation error |
| 3 | Click "Run Template" | Button shows "Running..." then reverts |
| 4 | Observe CLI output area | Output contains `> health.check`, run ID, status: `Completed`, and `Health check: OK` |
| 5 | Observe "Last run ID" below output | A valid GUID is displayed |

**Evidence:** Screenshot of CLI output showing successful health.check run.

---

## TST10-SC-003: Execute Template with Invalid Parameters

**Goal:** Verify error handling when passing unexpected parameters.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Select `health.check` in the template selector | Template selected |
| 2 | Enter `{"unexpected": "value"}` in the parameters textarea | JSON accepted by the input |
| 3 | Click "Run Template" | Error message appears in output and/or toast notification |
| 4 | Observe error text | Message indicates validation error for unexpected parameter |

**Evidence:** Screenshot of error output.

---

## TST10-SC-004: Execute Restricted Template (Insufficient Role)

**Goal:** Verify permission guidance when role is insufficient for a template.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Observe the restricted templates section (if visible) | Templates requiring higher role are listed with restricted label |
| 2 | Select a restricted template (e.g., `boards.list` requiring admin role) | Warning text shows "restricted for your role" |
| 3 | Click "Run Template" | Returns 403 Forbidden with actionable guidance message |
| 4 | Observe error message content | Message includes required role, current role, and guidance to Workspace > Settings |

**Evidence:** Screenshot of permission guidance message.

---

## TST10-SC-005: Log Query -- Broad Filter (All Levels, All Sources)

**Goal:** Verify broad log query returns entries after prior ops commands.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Click the "Logs" tab | Logs panel loads; entries appear (or empty state if no activity) |
| 2 | Run a `health.check` from CLI tab first, then return to Logs tab | Ensures at least some log entries exist |
| 3 | Set level filter to "All levels" and source filter to "all" | Filters are at defaults |
| 4 | Click "Refresh" | Log entries appear with timestamp, level, source, message columns |
| 5 | Verify at least one entry from `OpsCliService` source | Entry present from the prior command run |

**Evidence:** Screenshot of log entries with broad filter.

---

## TST10-SC-006: Log Query -- Level Filter

**Goal:** Verify log level filter narrows results correctly.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Navigate to Logs tab | Entries load |
| 2 | Set level filter to "Info" | Dropdown selection changes |
| 3 | Click "Refresh" | Only Info-level entries shown |
| 4 | Set level filter to "Error" | Dropdown selection changes |
| 5 | Click "Refresh" | Only Error-level entries shown (may be empty if no errors occurred) |

**Evidence:** Screenshots of filtered results for each level.

---

## TST10-SC-007: Log Query -- Source Filter

**Goal:** Verify source filter narrows results to a specific subsystem.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Navigate to Logs tab | Entries load |
| 2 | Type `OpsCliService` in the source filter field | Filter text accepted |
| 3 | Click "Refresh" | Only entries with source `OpsCliService` appear |
| 4 | Type a non-existent source (e.g., `NonexistentSource`) | Filter text accepted |
| 5 | Click "Refresh" | Empty state displayed with appropriate message |

**Evidence:** Screenshots of source-filtered results and empty state.

---

## TST10-SC-008: Log Query -- Correlation ID Filter

**Goal:** Verify correlation ID lookup returns run-correlated entries.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Run a `health.check` from CLI tab | Note the "Last run ID" displayed |
| 2 | Switch to Logs tab | Logs panel loads |
| 3 | Enter the correlation ID from the command run output into the correlation ID field | ID pasted |
| 4 | Click "Refresh" | Entries filtered to only those matching the correlation ID |
| 5 | Verify all returned entries share the same correlation ID | Correlation ID column matches on every row |

**Evidence:** Screenshot of correlation-filtered log entries.

---

## TST10-SC-009: Correlation ID -- Invalid/Nonexistent

**Goal:** Verify appropriate behavior for a nonexistent correlation ID.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Switch to Logs tab | Logs panel loads |
| 2 | Enter a fabricated correlation ID (e.g., `00000000000000000000000000000000`) | ID entered |
| 3 | Click "Refresh" | Empty state displayed: "No logs for this correlation ID" |
| 4 | Verify empty state guidance | Message suggests checking the ID, clearing filter, or refreshing |

**Evidence:** Screenshot of empty correlation state.

---

## TST10-SC-010: Correlation ID Propagation -- Request to Logs

**Goal:** Verify end-to-end correlation trail from HTTP request through command run to log entries.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Open browser DevTools Network tab | Network inspector ready |
| 2 | Run `health.check` from CLI tab | Observe the POST to `/api/ops/cli/run` |
| 3 | Note the `correlationId` field in the response JSON | ID recorded |
| 4 | Check response headers for `X-Request-Id` | Header present and matches the correlation ID from response body |
| 5 | Switch to Logs tab and query by that correlation ID | Entries returned matching the correlation |
| 6 | Use Endpoint Explorer tab: `GET /api/logs/correlation/{correlationId}` | Same entries returned via direct API call |

**Evidence:** Screenshots of network response, correlation in response body, and matching log entries.

---

## TST10-SC-011: Health Endpoint -- /health/live

**Goal:** Verify liveness probe returns healthy status.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Call `GET http://localhost:5000/health/live` (via browser, curl, or Endpoint Explorer) | 200 OK |
| 2 | Inspect response body | JSON with `status: "Healthy"` and `timestamp` field |
| 3 | Verify no authentication is required | Endpoint responds without bearer token |

**Evidence:** Response body screenshot or curl output.

---

## TST10-SC-012: Health Endpoint -- /health/ready (Full Contract)

**Goal:** Verify readiness probe returns comprehensive subsystem checks.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Call `GET http://localhost:5000/health/ready` | 200 OK (or 503 if workers still starting) |
| 2 | Inspect `checks.database` | Contains `status` field (Healthy/Unhealthy) |
| 3 | Inspect `checks.queue` | Contains `status`, `depth`, `totalDepth`, `captureDepth`, `threshold` |
| 4 | Inspect `checks.signalrBackplane` | Contains `status` (likely `NotConfigured` on local dev without Redis) |
| 5 | Inspect `checks.workers.queueToProposal` | Contains `status`, `lastHeartbeat`, `stalenessSeconds`, `maxStalenessSeconds` |
| 6 | Inspect `checks.workers.proposalHousekeeping` | Same structure as queueToProposal worker |
| 7 | Verify top-level `status` is `Ready` or `NotReady` | Consistent with individual check statuses |

**Evidence:** Full JSON response body.

---

## TST10-SC-013: Health Ready -- Worker Heartbeat Freshness

**Goal:** Verify worker heartbeat staleness tracks correctly over time.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Call `/health/ready` immediately after API startup (within 30s) | Worker status may show `Starting` (within startup grace period) |
| 2 | Wait 30+ seconds, call `/health/ready` again | Workers should show `Healthy` with recent `lastHeartbeat` |
| 3 | Inspect `stalenessSeconds` | Value is a reasonable number of seconds since last heartbeat |
| 4 | Verify `maxStalenessSeconds` is consistent with poll interval | queueToProposal: `max(pollInterval*3, 30)`; housekeeping: 180 |

**Evidence:** Two response bodies showing transition from Starting to Healthy.

---

## TST10-SC-014: Health Ready -- Queue Depth and Capture Backlog Separation

**Goal:** Verify queue depth excludes capture requests from the automation backlog count.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Create several capture items via `/workspace/inbox` or API | Capture requests queued |
| 2 | Call `/health/ready` | 200 OK |
| 3 | Inspect `checks.queue.depth` | Automation queue depth (should be 0 if no automation requests pending) |
| 4 | Inspect `checks.queue.captureDepth` | Should reflect captured items count |
| 5 | Inspect `checks.queue.totalDepth` | Should equal `depth + captureDepth` |

**Evidence:** Response body showing queue depth breakdown.

---

## TST10-SC-015: Health Ready -- SignalR Backplane Status (Local Dev)

**Goal:** Verify SignalR backplane reports correctly without Redis configured.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Call `/health/ready` on local dev (no Redis) | 200 OK |
| 2 | Inspect `checks.signalrBackplane` | Status is `NotConfigured` |
| 3 | Verify overall readiness is not degraded by unconfigured Redis | Top-level status is still `Ready` (assuming other checks pass) |

**Evidence:** Response body showing signalrBackplane: NotConfigured.

---

## TST10-SC-016: Operator Troubleshooting Journey

**Goal:** Validate the end-to-end operator diagnostic path: symptom detection, health check, log investigation, and correlation trail.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Start at `/workspace/ops/cli` | Ops console loaded |
| 2 | Run `health.check` template | Successful with output |
| 3 | Check `/health/ready` via Endpoint Explorer tab | Health status visible |
| 4 | Switch to Logs tab | Log entries load |
| 5 | Copy correlation ID from the health.check run | ID available |
| 6 | Paste correlation ID into Logs filter | Filtered to correlated entries |
| 7 | Verify the trail is coherent | Entries show the command lifecycle (start, execution, completion) |
| 8 | Clear filters and browse broader log view | All recent activity visible |

**Evidence:** Screenshots of each step in the troubleshooting chain.

---

## TST10-SC-017: Log Empty State -- No Matching Filters

**Goal:** Verify empty state messaging and recovery actions.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Navigate to Logs tab | Logs load |
| 2 | Set source filter to `NonexistentSource12345` | Filter applied |
| 3 | Click "Refresh" | Empty state panel appears |
| 4 | Verify empty state title | "No logs match the current filters" |
| 5 | Verify empty state body | Suggests broader filters or Review navigation |
| 6 | Click "Clear Filters" button | Filters reset; entries reappear |

**Evidence:** Screenshot of empty state and post-clear-filters state.

---

## TST10-SC-018: Log Empty State -- Correlation ID Not Found

**Goal:** Verify correlation-specific empty state.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Navigate to Logs tab | Logs load |
| 2 | Enter a nonexistent correlation ID | ID entered |
| 3 | Click "Refresh" | Empty state panel appears |
| 4 | Verify empty state title | "No logs for this correlation ID" |
| 5 | Verify empty state body | Suggests checking the ID or clearing the filter |

**Evidence:** Screenshot of correlation empty state.

---

## TST10-SC-019: Log Auto-Refresh Toggle

**Goal:** Verify auto-refresh polling behavior on the logs tab.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Navigate to Logs tab | Logs load |
| 2 | Check the "Auto refresh" checkbox | Checkbox is checked |
| 3 | Run a command from another tab/window | New log entry created |
| 4 | Wait 5-10 seconds on the Logs tab | New entry appears without manual refresh |
| 5 | Uncheck "Auto refresh" | Checkbox unchecked |
| 6 | Run another command from another tab/window | No automatic update observed on Logs tab |
| 7 | Click "Refresh" manually | New entries appear |

**Evidence:** Note timestamps of auto-refresh observations.

---

## TST10-SC-020: Endpoint Explorer -- Basic GET Request

**Goal:** Verify the endpoint explorer tab works for direct API probing.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Click "Endpoint Explorer" tab | Endpoint form loads with method selector and path input |
| 2 | Set method to GET, path to `/boards` | Inputs filled |
| 3 | Click "Send" | Response panel appears with status code and JSON body |
| 4 | Verify status code display | 200 shown in green (or appropriate code) |
| 5 | Verify response body | Valid JSON for the boards list |

**Evidence:** Screenshot of endpoint explorer with response.

---

## TST10-SC-021: Endpoint Explorer -- Health Endpoint Probing

**Goal:** Verify operators can probe health endpoints through the explorer.

**Caveat:** The endpoint explorer uses the axios HTTP client with `baseURL` set to `http://localhost:5000/api`. Paths entered in the explorer are relative to this base. Health endpoints live at `/health/*` (not under `/api/`), so probing `/health/ready` from the explorer sends the request to `http://localhost:5000/api/health/ready` which returns 404. To probe health endpoints, use curl or the browser address bar directly. This is a known UX limitation of the endpoint explorer.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Set method to GET, path to `/boards` | Path entered (a path that works under `/api/`) |
| 2 | Click "Send" | Response appears with valid JSON |
| 3 | For health endpoints, use curl: `curl http://localhost:5000/health/ready` | Full health payload visible |

**Evidence:** Screenshot of endpoint explorer response and/or curl output for health.

---

## TST10-SC-022: Ops CLI -- Reload Templates

**Goal:** Verify the reload templates button refreshes the template list.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Click "Reload Templates" button on CLI tab | No error; templates refresh |
| 2 | Verify template selector still contains expected templates | `health.check` and others present |

**Evidence:** Note before/after template count if different.

---

## TST10-SC-023: Concurrent Log Queries

**Goal:** Verify log queries work under concurrent access.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Open two browser tabs, both on `/workspace/ops/logs` | Both tabs load |
| 2 | Enable auto-refresh on both tabs | Both polling |
| 3 | Run commands from a third tab | Activity generated |
| 4 | Verify both log tabs update independently | Entries appear in both without interference or errors |

**Evidence:** Screenshots from both tabs showing independent results.

---

## TST10-SC-024: Cross-User Log Isolation

**Goal:** Verify log queries are scoped to the authenticated user.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Register/login as User A | Authenticated |
| 2 | Run `health.check` as User A | Command executed; note correlation ID |
| 3 | Register/login as User B in a different browser/incognito | Authenticated as different user |
| 4 | Navigate to Logs tab as User B | Logs load |
| 5 | Query logs broadly as User B | User A's ops entries are NOT visible to User B |
| 6 | Enter User A's correlation ID as User B | Returns 403 Forbidden or empty result (not User A's data) |

**Evidence:** Screenshots from both users showing isolation.

---

## TST10-SC-025: Tab Navigation and URL Routing

**Goal:** Verify tab switching updates the URL and direct URL access loads the correct tab.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Navigate to `/workspace/ops/cli` | CLI tab is active |
| 2 | Click "Endpoint Explorer" tab | URL changes to `/workspace/ops/endpoints` |
| 3 | Click "Logs" tab | URL changes to `/workspace/ops/logs` |
| 4 | Navigate directly to `/workspace/ops/logs` via address bar | Logs tab is active on load |
| 5 | Navigate directly to `/workspace/ops/endpoints` via address bar | Endpoint Explorer tab is active |

**Evidence:** Note URL changes at each step.

---

## Summary

| Scenario ID | Category | Description |
|---|---|---|
| TST10-SC-001 | Ops Console | Page load and template display |
| TST10-SC-002 | Ops CLI | health.check execution |
| TST10-SC-003 | Ops CLI | Invalid parameter error handling |
| TST10-SC-004 | Ops CLI | Restricted template permission guidance |
| TST10-SC-005 | Log Query | Broad filter query |
| TST10-SC-006 | Log Query | Level filter |
| TST10-SC-007 | Log Query | Source filter |
| TST10-SC-008 | Log Query | Correlation ID filter |
| TST10-SC-009 | Log Query | Nonexistent correlation ID |
| TST10-SC-010 | Correlation | End-to-end correlation propagation |
| TST10-SC-011 | Health | /health/live endpoint |
| TST10-SC-012 | Health | /health/ready full contract |
| TST10-SC-013 | Health | Worker heartbeat freshness |
| TST10-SC-014 | Health | Queue depth and capture backlog separation |
| TST10-SC-015 | Health | SignalR backplane local dev status |
| TST10-SC-016 | Ops Journey | End-to-end troubleshooting path |
| TST10-SC-017 | Log Query | Empty state with no matches |
| TST10-SC-018 | Log Query | Correlation empty state |
| TST10-SC-019 | Log Query | Auto-refresh toggle |
| TST10-SC-020 | Endpoint Explorer | Basic GET request |
| TST10-SC-021 | Endpoint Explorer | Health endpoint probing |
| TST10-SC-022 | Ops CLI | Reload templates |
| TST10-SC-023 | Log Query | Concurrent queries |
| TST10-SC-024 | Log Query | Cross-user isolation |
| TST10-SC-025 | Navigation | Tab routing and URL sync |
