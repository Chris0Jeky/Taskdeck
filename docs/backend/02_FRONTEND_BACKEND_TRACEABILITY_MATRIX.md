# Frontend-Backend Traceability Matrix

Last Updated: 2026-02-12

## 1. Purpose

Map frontend routes/capabilities to backend contracts so implementation order and ownership are unambiguous.

Status key:
- `ready`: backend contract implemented and actively usable
- `partial`: backend exists but activation gaps remain
- `missing`: backend contract not yet implemented

## 2. Route and Capability Matrix

| Frontend Surface | Frontend Route | Backend Contract | Status | Required Backend Work | Priority |
|---|---|---|---|---|---|
| Login | `/login` | `POST /api/auth/login` | ready | tighten failed-login 401 behavior and redirect guard tests | P0 |
| Register | `/register` | `POST /api/auth/register` | ready | add abuse/rate-limit policy and tests | P1 |
| Password Change | `/workspace/settings/profile` | `POST /api/auth/change-password` | partial | require claim actor and remove body/query actor dependency | P0 |
| Boards List | `/workspace/boards` | `GET /api/boards` | partial | add `[Authorize]` and board read policy enforcement | P0 |
| Board Detail | `/workspace/boards/:id` | `GET /api/boards/{id}` | partial | enforce per-board access policy | P0 |
| Board Create/Edit/Delete | board UI actions | `POST/PUT/DELETE /api/boards` | partial | add auth/policy + audit logging | P0 |
| Columns CRUD/Reorder | board UI actions | `GET/POST/PATCH/DELETE/POST reorder /api/boards/{id}/columns` | partial | add auth/policy + audit logging | P0 |
| Cards CRUD/Move | board UI actions | `GET/POST/PATCH/POST move/DELETE /api/boards/{id}/cards` | partial | add auth/policy + audit logging | P0 |
| Labels CRUD | board UI actions | `GET/POST/PATCH/DELETE /api/boards/{id}/labels` | partial | add auth/policy + audit logging | P0 |
| Board Access | `/workspace/access` | `GET/POST/PUT/DELETE /api/boards/{id}/access` | partial | remove transitional actor query parameters, use claim actor | P0 |
| Activity Timeline | `/workspace/activity/*` | `GET /api/audit/boards/{id}`, `GET /api/audit/entities/...`, `GET /api/audit/users/{id}` | partial | enforce read policies + pagination bounds | P1 |
| Queue Dashboard | `/workspace/automations/queue` | `POST /api/llm-queue`, status/user/stats/cancel/process-next | partial | claim identity, worker handoff, correlation and audit | P0 |
| Proposals | `/workspace/automations/proposals` | `/api/automation/proposals*` | missing | implement proposal CRUD + approve/reject/edit/diff | P0 |
| Ops CLI | `/workspace/ops/cli` | `POST /api/ops/cli/run`, `GET /api/ops/cli/runs/{id}` | missing | implement allowlisted command execution | P1 |
| Ops Endpoint Explorer | `/workspace/ops/endpoints` | metadata/diagnostic endpoints | partial | add endpoint catalog endpoint and permission checks | P2 |
| Ops Logs | `/workspace/ops/logs` | `GET /api/logs`, `/stream`, `/correlation/{id}` | missing | implement query, filter, stream, and correlation lookup | P1 |
| Export/Import | `/workspace/settings/export-import` | `GET /api/export/boards/{id}`, `POST /api/import/boards*` | partial | enforce claims-based actor, add stronger validation and audit | P1 |
| Archive | `/workspace/archive` | `GET /api/archive/items`, `POST /api/archive/{entityType}/{id}/restore` | missing | implement archive inventory and restore flow | P0 |
| Chat Command Window | planned frontend surface | `POST /api/llm/chat/sessions`, `POST /messages`, stream endpoint | missing | implement chat session/message pipeline | P1 |

## 3. Contract Delta Summary

### 3.1 Contracts already available and reusable
- auth endpoints
- core board/column/card/label endpoints
- board access endpoints
- audit endpoints
- export/import endpoints
- llm queue endpoints

### 3.2 Contracts requiring enforcement activation
- all core mutation endpoints need explicit authz enforcement
- all actor identity query/body parameters need claim-based replacement
- all relevant endpoints need standardized correlation/audit behavior

### 3.3 Contracts missing entirely
- archive recovery API
- automation proposal lifecycle API
- ops CLI bridge API
- logs query and stream APIs
- chat session and message APIs

## 4. Ownership and Sequence

1. `P0`: Auth/authz enforcement and missing P0 contracts (proposal + archive).
2. `P1`: Ops/logs/chat + reliability workers.
3. `P2`: Endpoint explorer metadata and refinements.

## 5. Exit Criteria for Traceability

Traceability is complete when:
- every frontend route has a concrete backend contract and test reference,
- no route is blocked by `missing` status for planned release slice,
- each endpoint has a documented policy, payload contract, and failure behavior.
