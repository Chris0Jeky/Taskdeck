# Failure-Injection Drill Suite

Last Updated: 2026-03-28
Issue: `#149` TST-13 deployment and MCP failure-injection drill suite

## Overview

This drill suite provides repeatable failure-injection scenarios for Taskdeck deployment and MCP workflows. Each drill simulates a specific failure condition, verifies that the system handles it gracefully, classifies the failure cause, and documents the recovery path.

## Quick Start

Run all drills from the repository root:

```bash
bash scripts/drills/run-all-drills.sh
```

CI-compatible mode (emits machine-readable summary):

```bash
bash scripts/drills/run-all-drills.sh --ci
```

Artifacts (logs, captured output) are written to `drill-artifacts/` in the repo root.

## Drill Scenarios

### Drill 1: Missing SQLite Database (`drill-db-missing.sh`)

| Field | Value |
| --- | --- |
| Script | `scripts/drills/drill-db-missing.sh` |
| Category | Startup dependency / persistence |
| Simulates | Application starts with a non-existent SQLite DB file |
| Expected behavior | EF Core SQLite provider auto-creates the database; health endpoint returns 200 |
| Pass criteria | API becomes healthy within 30s, DB file is created |
| Failure signal | API crashes or health endpoint unreachable |

**Recovery path:**

1. Ensure the database directory exists and is writable
2. Verify that `EnsureCreated()` or EF migrations run on startup
3. Check filesystem permissions (especially in container environments)
4. If the directory itself is missing, create it before starting the app

**CI compatibility:** Runs without Docker. Requires `dotnet` CLI, or falls back to static analysis of `appsettings.json`.

---

### Drill 2: Locked SQLite Database (`drill-db-locked.sh`)

| Field | Value |
| --- | --- |
| Script | `scripts/drills/drill-db-locked.sh` |
| Category | Startup dependency / database lock |
| Simulates | SQLite database file is locked by another process |
| Expected behavior | API starts but reports degraded health (503) or handles lock with retry/timeout |
| Pass criteria | API responds (200 or 503) rather than crashing silently |
| Failure signal | API unresponsive; silent crash |

**Recovery path:**

1. Identify the locking process: `lsof <db-path>` (Linux) or `handle.exe` (Windows)
2. Wait for the lock to release, or terminate the locking process
3. If stale WAL: remove `.db-wal` and `.db-shm` after confirming no active writers
4. Add `Busy Timeout=5000` to the SQLite connection string for transient lock resilience

**CI compatibility:** Requires `dotnet` CLI and optionally `sqlite3` or `flock`. Falls back to static analysis of Infrastructure layer code.

---

### Drill 3: Startup Timeout (`drill-startup-timeout.sh`)

| Field | Value |
| --- | --- |
| Script | `scripts/drills/drill-startup-timeout.sh` |
| Category | Startup timeout / readiness delay |
| Simulates | Readiness poll against an unavailable endpoint with a short timeout |
| Expected behavior | Polling loop times out cleanly with a clear error |
| Pass criteria | Timeout fires within expected window; startup scripts have timeout parameters; compose has healthcheck config |
| Failure signal | Infinite hang, missing timeout config |

**Recovery path:**

1. Increase `ReadyTimeoutSeconds` if the service legitimately needs more startup time
2. Investigate slow startup causes: heavy EF migrations, external dependency waits
3. Add Docker healthcheck with appropriate `start_period` and `interval`
4. Investigate external dependencies blocking startup (network, DNS)
5. For Kubernetes: add startup probes; for Compose: use `depends_on` conditions

**CI compatibility:** Fully CI-compatible. No Docker or `dotnet` required; uses static analysis and curl against a dead port.

---

### Drill 4: MCP Configuration Validation (`drill-mcp-invalid-credentials.sh`)

| Field | Value |
| --- | --- |
| Script | `scripts/drills/drill-mcp-invalid-credentials.sh` |
| Category | MCP configuration / unknown-server handling |
| Simulates | Missing MCP helper scripts, optional-server classification drift, or an unknown server name passed to the MCP gateway |
| Expected behavior | Credential-management helpers exist, default servers remain unaffected, and bogus server names fail clearly at dry-run time |
| Pass criteria | Credential management scripts exist with validation; profile test distinguishes optional servers; unknown-server dry-run fails; LLM provider has Mock fallback |
| Failure signal | No credential management scripts; optional/required handling missing; bogus server dry-run unexpectedly succeeds |

**Recovery path:**

1. Set MCP secrets: `echo '<key>' | docker mcp secret set <server>.<secret-name>`
2. Rotate expired tokens via the provider portal, then re-set via Docker MCP CLI
3. For LLM providers: set `Llm__Provider=Mock` for safe local fallback
4. For optional servers: use `-SkipOptionalWhenMissingPrereqs` flag
5. See `scripts/mcp/Set-MarketplaceMcpCredentials.ps1` for credential setup

**Scope note:** This drill does not currently inject a known-bad secret into a real optional MCP server. It validates gateway/config behavior and unknown-server failure handling only.

**CI compatibility:** Static analysis always runs. Live Docker MCP tests run only when Docker Desktop MCP is available (skipped gracefully in CI).

---

### Drill 5: Reverse-Proxy Misconfiguration (`drill-proxy-misconfiguration.sh`)

| Field | Value |
| --- | --- |
| Script | `scripts/drills/drill-proxy-misconfiguration.sh` |
| Category | Proxy misconfiguration / security headers |
| Simulates | Regression check on nginx configuration for required security headers and dangerous directives |
| Expected behavior | All required headers present; no dangerous directives; proper service wiring |
| Pass criteria | All 5 security headers configured; no autoindex/server_tokens/unsafe directives |
| Failure signal | Missing security headers or dangerous directives found |

Required security headers checked:
- `X-Content-Type-Options`
- `X-Frame-Options`
- `Referrer-Policy`
- `Permissions-Policy`
- `Content-Security-Policy`

**Recovery path:**

1. Add missing security headers to nginx config in `deploy/nginx/`
2. Remove dangerous directives (`autoindex on`, `server_tokens on`)
3. Ensure proxy `depends_on` backend service in compose
4. Run: `powershell -File scripts/deploy/Verify-TaskdeckDeploymentHardening.ps1`
5. Verify headers with: `curl -I http://localhost:8080/`

**CI compatibility:** Fully CI-compatible. No Docker or running services required; performs static analysis of nginx config files.

## Drill Output Contract

Each drill script:

1. Accepts the repo root as the first positional argument (defaults to `.`)
2. Prints structured output with `[drill-name]` prefix on each line
3. Classifies the failure cause with a `CAUSE CLASSIFICATION:` line
4. Documents recovery steps with `RECOVERY:` lines
5. Ends with `PASS` or `FAIL` on the final line
6. Exits 0 on pass, non-zero on fail

The orchestrator (`run-all-drills.sh`) captures all output to `drill-artifacts/<drill-name>.log` and emits a summary with total/passed/failed counts.

### CI artifact output (with `--ci` flag):

```
DRILL_SUITE_TOTAL=5
DRILL_SUITE_PASSED=5
DRILL_SUITE_FAILED=0
```

## Adding New Drills

1. Create a new script in `scripts/drills/` following the naming convention `drill-<scenario>.sh`
2. Follow the output contract above (prefixed lines, cause classification, recovery, PASS/FAIL)
3. Register the drill in the `DRILLS` array in `run-all-drills.sh`
4. Add a scenario section to this document
5. Ensure the drill can fall back to static analysis when runtime prerequisites are unavailable

## Related Documentation

- `docs/ops/DEPLOYMENT_HARDENING_MATRIX.md` — deployment hardening verification matrix
- `docs/ops/DEPLOYMENT_CONTAINERS.md` — container deployment reference
- `scripts/deploy/` — deployment automation scripts
- `scripts/mcp/` — MCP configuration and testing scripts
