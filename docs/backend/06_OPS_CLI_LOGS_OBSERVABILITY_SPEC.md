# Ops CLI, Logs, and Observability Specification

Last Updated: 2026-02-12

## 1. Objective

Back the frontend ops console with safe backend operations:
- execute only allowlisted operational commands,
- inspect command execution lifecycle and logs,
- query and stream structured application logs with correlation support.

## 2. Endpoint Contract

CLI bridge:
- `POST /api/ops/cli/run`
- `GET /api/ops/cli/runs/{id}`
- `GET /api/ops/cli/runs/{id}/logs`

Logs:
- `GET /api/logs`
- `GET /api/logs/stream`
- `GET /api/logs/correlation/{correlationId}`

Optional:
- `GET /api/ops/endpoints` (endpoint catalog for explorer panel)

## 3. Command Execution Model

### 3.1 Allowlist policy

No raw shell execution from client input.

Allowed command templates are server-defined:
- `boards.list`
- `boards.search`
- `queue.stats`
- `queue.pending`
- `health.check`

Each template contains:
- executable path/verb,
- accepted parameters schema,
- risk class (`ReadOnly`, `SafeWrite`, `Restricted`),
- timeout budget,
- required role.

### 3.2 Execution lifecycle

States:
- `Queued`
- `Running`
- `Completed`
- `Failed`
- `TimedOut`
- `Cancelled`

Command run response includes:
- `runId`
- `status`
- `startedAt`
- `completedAt`
- `exitCode` (if applicable)
- `truncated` (if output clipped)
- `correlationId`

## 4. Logs Model

Standard log fields:
- `timestamp`
- `level`
- `source`
- `eventName`
- `message`
- `correlationId`
- `userId` (if authenticated)
- `boardId` (if scoped)
- `metadata` (JSON)

Retention:
- hot log query window default 7 days,
- hard max query window 30 days unless privileged role.

## 5. Streaming Strategy

Default transport: SSE.

Rules:
- heartbeat event every 15 seconds,
- max stream lifetime 10 minutes before re-subscribe,
- filter criteria locked per stream connection.

## 6. Security and Permission Model

Roles:
- viewer: no ops endpoints
- editor: read logs for permitted boards
- admin: run read-only templates + broader logs
- owner/system admin: run safe write templates and diagnostic commands

Constraints:
- request rate limit for run endpoint,
- output size cap,
- prohibited token/pattern redaction in output and logs.

## 7. Observability Standards

Required telemetry:
- command run count and failure rate by template,
- p50/p95 runtime,
- log query latency,
- stream client disconnect rate,
- correlation lookup latency.

Required tracing:
- include `X-Request-Id` and W3C `traceparent` propagation where available.

## 8. Failure and Guardrail Behavior

- unsupported template -> `400 ValidationError`
- unauthorized template -> `403 Forbidden`
- timeout -> `408` or failed status with timeout code
- command output overflow -> truncate and mark `truncated=true`

All failures must:
- emit structured error logs,
- preserve correlation IDs,
- be queryable by `GET /api/logs/correlation/{id}`.

## 9. Test Requirements

Unit:
- template validator,
- parameter schema validator,
- permission gate logic.

Integration:
- run/list/log retrieval flow,
- timeout and truncation behavior,
- log filtering and correlation lookup.

E2E:
- execute safe CLI template from UI,
- verify result and linked logs in logs panel.

## 10. Acceptance Criteria

- no direct arbitrary shell execution is possible,
- all command runs are auditable and role-gated,
- logs query and stream are stable and correlated with user actions.
