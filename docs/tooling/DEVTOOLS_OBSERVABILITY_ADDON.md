# Taskdeck - DevTools & Observability Add-On (Agent + Human)

**Purpose:** Extend your harness so Codex can debug UI/network/performance issues quickly and reason from runtime signals (logs/metrics/traces), not guesses.

This doc is designed to be referenced from `AGENTS.md` (short pointer) and used during:
- UI bug investigations
- flaky E2E debugging
- performance regressions
- automation execution issues
- works locally but not in CI problems

---

## 1) When to use what (Playwright vs DevTools vs Logs)

### Use Playwright MCP when you need:
- deterministic reproduction of a user flow
- a regression test (smoke/E2E)
- screenshots/videos from a controlled run
- stable DOM assertions

### Use Chrome DevTools (CDP / DevTools MCP) when you need:
- console errors and stack traces
- network request inspection (headers, payloads, status)
- performance profiling (long tasks, layout thrash, CPU/memory)
- layout/debug overlays, event listeners, DOM mutation tracing
- to understand why (Playwright proves it breaks; DevTools explains why)

### Use Observability (logs/metrics/traces) when:
- behavior depends on timing/background workers
- you need system-level truth (errors, durations, failure rates)
- you're triaging CI-only failures or intermittent bugs

---

## 2) Standard UI-debug workflow (agent-friendly)

### Step 0 - Write the repro contract
Before touching code, record:
- expected behavior
- actual behavior
- minimal repro steps
- environment (OS, browser, build, branch)
- stop condition (what result counts as fixed)

### Step 1 - Reproduce with Playwright first
- Navigate to the failing screen/flow.
- Capture screenshot on failure.
- Extract visible text and key DOM state.
- If easy: create a minimal regression test.

### Step 2 - Escalate to DevTools signals
Capture these signals:
- console log/errors during repro
- network: failing requests, status codes, payload shape mismatches
- timing: which actions trigger long tasks
- storage/auth: cookies/local storage/session state

### Step 3 - Close the loop: convert debug signal to a guardrail
Prefer converting learnings into:
- a regression test (Playwright)
- an API integration test (if server contract)
- a lint/structural check (if architecture drift)
- a doc contract update (if behavior is intended but undocumented)

## Required Artifacts Per Investigation

- Repro steps (versioned, not ad-hoc).
- One UI artifact:
  - screenshot or video snippet.
- One protocol artifact:
  - failing request details (URL, status, response shape) or equivalent DevTools trace.
- One backend artifact:
  - log or trace evidence with correlation ID.
- One guardrail:
  - test/check/doc update preventing recurrence.

## Command Baseline

UI and regression:
- `cd frontend/taskdeck-web; npx playwright test --reporter=line`
- `cd frontend/taskdeck-web; npx vitest --run --reporter=verbose`

MCP profile and optional servers:
- `powershell -File ./scripts/mcp/Test-DockerMcpProfile.ps1`
- `powershell -File ./scripts/mcp/Test-DockerMcpProfile.ps1 -IncludeOptional`
- strict mode: `powershell -File ./scripts/mcp/Test-DockerMcpProfile.ps1 -IncludeOptional -FailOnOptionalErrors`

Deployment/runtime smoke:
- `powershell -File ./scripts/deploy/Start-TaskdeckStack.ps1`
- `powershell -File ./scripts/deploy/Smoke-TestTaskdeckStack.ps1`

Docs and ops governance:
- `node scripts/check-docs-governance.mjs`
- `node scripts/check-github-ops-governance.mjs`

---

## 3) Minimal DevTools integration plan (choose one)

### Option A: Use DevTools manually, attach artifacts
**Fastest:** no new MCP needed.
- Run repro in Chrome
- Export HAR for network issues
- Screenshot console errors
- Save performance profile if needed
- Attach artifacts to issue/PR

**Use when:** you only occasionally need deep debugging.

### Option B: Add a Chrome DevTools MCP server
**Goal:** agent can programmatically collect console/network/perf evidence.

**Recommended tool behaviors:**
- open URL
- capture console logs
- capture network log / HAR
- take screenshot
- export performance trace (optional)

**Safety:** DevTools tools are powerful; restrict to read/observe operations where possible.

### Option C: Bake CDP capture into Playwright runs
Playwright can capture traces/video/network-like signals depending on your setup.
- This keeps a single automation surface (Playwright), but gives richer evidence.

**Use when:** you want minimum tool surface.

---

## 4) Observability: lite to full roadmap

### 4.1 Lite (do now if needed; minimal infra)
**Goal:** high-signal logs you can query quickly.

**Guidelines**
- Structured logs (JSON) wherever possible
- Always include:
  - timestamp
  - level
  - requestId / correlationId
  - userId (only if non-sensitive and appropriate)
  - area (api/worker/automation/ops)
  - durationMs (for hot paths)
- Never log secrets/tokens/PII.

**Queryable storage**
- local file logs (rotating)
- basic query scripts (PowerShell/node)
- or log search via your existing Taskdeck Logs feature (if its already good)

### 4.2 Medium (still local)
Add:
- simple metrics counters (success/failure counts, durations)
- structured event logs for:
  - auth failures
  - automation execution steps
  - archive restore conflicts
- one-click bundle for bug reports (logs + environment + last actions)

### 4.3 Full (if/when the project warrants it)
If you want the OpenAI-style harness:
- logs -> Loki (LogQL)
- metrics -> Prometheus (PromQL)
- traces -> Tempo/Jaeger (TraceQL)

This is optional; do it when debugging time justifies the setup.

---

## 5) Correlation ID contract (high leverage)
**Goal:** connect FE events -> API requests -> worker actions -> logs.

### Frontend
- Generate/request a requestId per API call (or per user action).
- Send it in an HTTP header (e.g., `X-Request-Id`).

### Backend
- Accept requestId from header or generate if missing.
- Include it in:
  - response headers
  - logs
  - internal calls/events

### Workers/automation
- Propagate requestId into job metadata and logs.

**Deliverable:** Given a bug report with requestId, we can trace the entire lifecycle.

---

## 6) Agent operating rules for DevTools/Observability work

### Output requirements
When the agent investigates a bug using these signals, it must produce:
- repro steps
- evidence summary:
  - console errors (if any)
  - failing network call details (URL, status, response shape)
  - relevant log lines (with requestId)
- fix summary
- new/updated guardrail (test/check/doc)
- commands run + results

### Stop conditions
If evidence suggests:
- data loss risk
- security boundary regression
- a broad architectural change is required
Stop and propose options instead of shipping a risky change.

---

## 7) Where to link this doc
Add a short pointer from:
- root `AGENTS.md` (tool selection)
- `frontend/AGENTS.md` (Playwright/DevTools usage)
- `backend/AGENTS.md` (logs/correlation/diagnosability)
