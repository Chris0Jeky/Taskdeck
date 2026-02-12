# Ops Console and Logs Specification

Last Updated: 2026-02-12
Status: Design complete, implementation pending

## 1. Purpose

Provide an operator-focused interface for:
- executing allowlisted CLI actions,
- calling API endpoints for diagnostics,
- observing structured logs and traces.

This directly addresses requirements for CLI exposure and improved testing feedback loops.

## 2. Target Surfaces

Routes:
- `/workspace/ops/cli`
- `/workspace/ops/endpoints`
- `/workspace/ops/logs`

Core components:
- command template picker
- command parameter form
- run console with stdout/stderr/exit code
- endpoint explorer request builder
- response viewer with JSON formatting
- logs table and live stream panel

## 3. CLI Execution Model

Security-first constraints:
- only allowlisted commands can run from UI
- no shell string passthrough from free-form user input
- command execution under explicit role checks
- store and display execution metadata

Command run record fields:
- `runId`
- `commandKey`
- `args`
- `requestedBy`
- `startedAt`
- `endedAt`
- `status`
- `stdout`
- `stderr`
- `exitCode`
- `correlationId`

## 4. Endpoint Explorer Model

Capabilities:
- select endpoint from capability registry
- auto-render required parameters/body schema
- execute request with auth context
- inspect status code, headers, body, and error mapping

Use cases:
- debugging backend contracts
- manual QA of edge paths
- reproducing failed UI mutations

## 5. Logs and Traceability

Required log dimensions:
- timestamp
- level (`info`, `warning`, `error`)
- source (`frontend`, `api`, `cli-bridge`, `queue`, `automation`)
- correlation ID
- actor/user ID
- entity reference (`boardId`, `cardId`, etc)
- message and structured payload

Log modes:
- pull mode: paged/filterable query
- stream mode: SSE/WebSocket for near-real-time feed

## 6. Correlation Strategy

Each user-triggered operation should have a correlation ID that propagates through:
- frontend action
- API request
- service/repository logs
- CLI bridge logs (when relevant)

UI must expose correlation ID on:
- action result toasts (expandable)
- failed operation drawer
- logs explorer rows

## 7. Required Backend Additions

CLI bridge endpoints:
- `POST /api/ops/cli/run`
- `GET /api/ops/cli/runs/{runId}`
- `GET /api/ops/cli/runs/{runId}/logs`

Logs endpoints:
- `GET /api/logs`
- `GET /api/logs/stream`

Optional enhancement:
- `GET /api/logs/correlation/{correlationId}`

## 8. Role and Permission Policy

Recommended role requirements:
- Viewer: view-only logs for accessible boards
- Editor: view logs + run safe read commands
- Admin/Owner: run full allowlisted command set and endpoint diagnostics

Sensitive operations require explicit confirmation.

## 9. UX Requirements

CLI panel:
- templates grouped by domain (`boards`, `cards`, `columns`, `labels`, `queue`)
- command preview before execution
- clear failure reasons and remediation hints

Endpoint explorer:
- schema-assisted forms
- persistent request history per user
- copy-as-curl and copy-json actions

Logs panel:
- filter by level/source/time/correlation
- open detail drawer for structured payload
- link back to related entity view

## 10. Testing Requirements

Unit:
- capability registry parsing
- run state reducer transitions

Integration:
- command submit/poll/result rendering
- endpoint explorer request/response handling
- log filtering and detail expansion

E2E:
- run allowlisted command from UI and verify output visibility
- execute endpoint explorer request and inspect formatted response
- trace failed board mutation via correlation ID in logs panel

## 11. Definition of Done

Ops/logs slice is complete when:
- CLI and endpoint explorer are usable by keyboard,
- logs are filterable and tied to frontend actions,
- failed operations can be diagnosed end-to-end without leaving UI,
- role restrictions are enforced and visible.
