# Endpoint to Frontend Entry-Point Matrix

Last Updated: 2026-02-12
Status: Decision-complete mapping for redesign implementation

## 1. Purpose

This document maps backend API surface to concrete frontend entry points, interaction patterns, identity handling, and test requirements.

Read this together with:
- `docs/frontend/01_OVERHAUL_ARCHITECTURE.md`
- `docs/frontend/04_AUTH_PERMISSIONS_ROLLOUT_SPEC.md`

## 2. Conventions

Columns:
- `Endpoint`: API route and method
- `Frontend Entry Point`: primary route/view or panel
- `Now`: behavior in current backend/frontend state
- `Target`: intended steady-state behavior
- `Test Coverage`: minimum automated coverage required

## 3. Auth Surface

| Endpoint | Frontend Entry Point | Now | Target | Test Coverage |
|---|---|---|---|---|
| `POST /api/auth/login` | `/login` form submit | no UI currently | session token bootstrap + redirect to workspace | unit + integration + E2E login success/failure |
| `POST /api/auth/register` | `/register` form submit | no UI currently | self-registration + immediate signed-in session | integration + E2E |
| `POST /api/auth/change-password` | `/workspace/settings/profile` security section | no UI currently; request includes `userId` | claim-based actor, no user picker | integration + E2E |

## 4. Users Surface

| Endpoint | Frontend Entry Point | Now | Target | Test Coverage |
|---|---|---|---|---|
| `GET /api/users` | `/workspace/settings/access` user directory | no UI currently | searchable user picker for board access and ownership tasks | unit + integration |
| `GET /api/users/{id}` | profile drawer and access detail panels | no UI currently | display user metadata and status | integration |
| `GET /api/users/by-username/{username}` | login recovery and admin lookup tool | no UI currently | diagnostics and quick lookup use only | integration |
| `POST /api/users` | admin user create flow (optional) | no UI currently | restricted to owner/admin tools | integration |
| `PUT /api/users/{id}` | profile editor | no UI currently | self-profile edit and admin edit | integration + E2E |
| `POST /api/users/{id}/deactivate` | user row action | no UI currently | status control with confirmation + audit note | integration + E2E |
| `POST /api/users/{id}/activate` | user row action | no UI currently | status restore flow | integration + E2E |

## 5. Boards/Columns/Cards/Labels (Core Workspace)

| Endpoint | Frontend Entry Point | Now | Target | Test Coverage |
|---|---|---|---|---|
| `GET /api/boards` | `/workspace/boards` list | already used in legacy view | role-aware filtered board list + owner/access badges | existing + new integration |
| `GET /api/boards/{id}` | `/workspace/boards/:boardId` | already used | board header + metadata panel + access summary | existing + new integration |
| `POST /api/boards` | board create command (button + palette) | already used | create with owner context + post-create focus flow | unit + integration + E2E |
| `PUT /api/boards/{id}` | board settings drawer | already used | split content updates vs archive toggle by permission | integration + E2E |
| `DELETE /api/boards/{id}` | board danger zone | already used | permission-gated delete/archive semantics with rollback messaging | integration + E2E |
| `GET /api/boards/{boardId}/columns` | board lane bootstrap | implicit in current board fetch path | explicit column refresh and optimistic reorder reconciliation | unit + integration |
| `POST /api/boards/{boardId}/columns` | add column action | already used | keyboard-first inline create + permission precheck | existing + new E2E |
| `PATCH /api/boards/{boardId}/columns/{columnId}` | column inspector editor | already used | constrained by write permission + WIP hints | unit + integration |
| `DELETE /api/boards/{boardId}/columns/{columnId}` | column danger action | already used | conflict-friendly UI (cards present) + guidance | integration + E2E |
| `POST /api/boards/{boardId}/columns/reorder` | drag/drop and keyboard reorder commands | already used | atomic reorder with optimistic fallback and retry | unit + integration + E2E |
| `GET /api/boards/{boardId}/cards` | board card loader/filter pipeline | already used | advanced filtering + saved views (future) | existing + new integration |
| `POST /api/boards/{boardId}/cards` | quick-add and inspector create | already used | keyboard default create path + validation messages | existing + new E2E |
| `PATCH /api/boards/{boardId}/cards/{cardId}` | card inspector editor | already used | section-level save actions and draft state | unit + integration + E2E |
| `POST /api/boards/{boardId}/cards/{cardId}/move` | drag/drop + keyboard move command | already used | deterministic move with WIP pre-check hints | existing + new E2E |
| `DELETE /api/boards/{boardId}/cards/{cardId}` | card danger action | already used | soft-delete pathway when archive surface exists | integration + E2E |
| `GET /api/boards/{boardId}/labels` | label manager drawer | already used | label taxonomy panel + quick filters | unit + integration |
| `POST /api/boards/{boardId}/labels` | label create action | already used | accessible color and contrast validation | unit + integration |
| `PATCH /api/boards/{boardId}/labels/{labelId}` | label editor | already used | inline rename/color update with preview | integration |
| `DELETE /api/boards/{boardId}/labels/{labelId}` | label delete action | already used | impact warning (cards affected) | integration + E2E |

## 6. Board Access and Permissions

| Endpoint | Frontend Entry Point | Now | Target | Test Coverage |
|---|---|---|---|---|
| `GET /api/boards/{boardId}/access` | board members panel | no UI currently | role table with granted-by metadata | integration + E2E |
| `POST /api/boards/{boardId}/access?grantedBy=...` | invite/add member action | no UI currently | transitional query parameter adapter then claim-based actor | integration + E2E |
| `PUT /api/boards/{boardId}/access/{accessId}?updatedBy=...` | role change dropdown | no UI currently | role escalation safeguards + owner constraints | integration + E2E |
| `DELETE /api/boards/{boardId}/access/{accessId}?revokedBy=...` | remove access action | no UI currently | confirmation flow + dependency warnings | integration + E2E |

## 7. Export/Import Surface

| Endpoint | Frontend Entry Point | Now | Target | Test Coverage |
|---|---|---|---|---|
| `GET /api/export/boards/{boardId}?userId=...` | export dialog (`json bundle`) | no UI currently | action available in settings/export-import | integration + E2E |
| `GET /api/export/boards/{boardId}/json?userId=...` | export raw JSON action | no UI currently | pretty viewer + download + copy | integration |
| `POST /api/import/boards?userId=...` | import structured form | no UI currently | preview validation before commit | integration + E2E |
| `POST /api/import/boards/json?userId=...` | import raw JSON editor/upload | no UI currently | parse/validate/report with conflicts surfaced | integration + E2E |

## 8. Audit Surface

| Endpoint | Frontend Entry Point | Now | Target | Test Coverage |
|---|---|---|---|---|
| `GET /api/audit/boards/{boardId}?limit=...` | `/workspace/activity/board/:boardId` | no UI currently | timeline with filters and pagination limit control | integration + E2E |
| `GET /api/audit/entities/{entityType}/{entityId}?limit=...` | entity detail activity tab | no UI currently | local activity context in inspector panel | integration |
| `GET /api/audit/users/{userId}?limit=...` | user activity tab | no UI currently | cross-board activity stream for admin/owner | integration |

## 9. LLM Queue Surface

| Endpoint | Frontend Entry Point | Now | Target | Test Coverage |
|---|---|---|---|---|
| `POST /api/llm-queue` | new automation request composer | no UI currently | request submit with payload schema assistance | integration + E2E |
| `GET /api/llm-queue/user/{userId}` | queue list by user | no UI currently | transitional identity adapter then claim mode | integration |
| `GET /api/llm-queue/status/{status}` | queue status tabs | no UI currently | status-segmented queue monitor | integration + E2E |
| `POST /api/llm-queue/{requestId}/cancel?userId=...` | queue item cancel action | no UI currently | cancellation with permission and reason UX | integration + E2E |
| `POST /api/llm-queue/process-next` | manual process action in automation center | no UI currently | operator control until background worker activation | integration + E2E |
| `GET /api/llm-queue/stats` | automation dashboard summary | no UI currently | metric cards and trend snapshots | integration |

## 10. Required New API Surfaces (To Satisfy Personal Notes)

These surfaces are not currently active and must be added before corresponding UI is fully enabled:

| Proposed Endpoint | Frontend Entry Point | Purpose |
|---|---|---|
| `POST /api/ops/cli/run` | `/workspace/ops/cli` | run allowlisted CLI commands safely from UI |
| `GET /api/ops/cli/runs/{id}` | `/workspace/ops/cli` | fetch command status + outputs |
| `GET /api/ops/cli/runs/{id}/logs` | `/workspace/ops/logs` | inspect execution logs |
| `GET /api/logs` | `/workspace/ops/logs` | query logs by level/source/window |
| `GET /api/logs/stream` | `/workspace/ops/logs` | near-real-time stream (SSE/WebSocket) |
| `GET /api/archive/items` | `/workspace/archive` | list recoverable archived entities |
| `POST /api/archive/{entityType}/{id}/restore` | `/workspace/archive` | restore archived board/column/card |
| `POST /api/automation/proposals` | `/workspace/automations/proposals` | create proposal from manual or agent input |
| `GET /api/automation/proposals` | `/workspace/automations/proposals` | list pending/approved/rejected proposals |
| `POST /api/automation/proposals/{id}/approve` | proposal detail action | approved apply flow |
| `POST /api/automation/proposals/{id}/reject` | proposal detail action | explicit reject with reason |
| `POST /api/automation/proposals/{id}/edit` | proposal detail action | edit proposal before apply |

## 11. Identity Transition Rule

Current backend includes mixed identity modes.

Transition strategy:
1. Phase A: UI uses transitional adapters where endpoint requires query/body actor id.
2. Phase B: backend rollout enforces claims on core and side-track surfaces.
3. Phase C: remove transitional actor-id plumbing from frontend and tests.

No endpoint should remain dual-mode beyond two sprints after claims enforcement lands.

## 12. Error Mapping Standard for UI

All feature modules must map errors from `{ errorCode, message }` to:
- user-facing toast message
- action-scoped inline message
- trace log row (endpoint + request id + timestamp)

Minimum error code handling:
- `ValidationError`
- `NotFound`
- `AuthenticationFailed`
- `Forbidden`
- `Conflict`
- `WipLimitExceeded`

## 13. Matrix Completion Criteria

This matrix is considered complete when:
- each active backend endpoint has a documented UI entry point,
- each planned frontend surface has explicit endpoint dependencies,
- test coverage expectations are assigned,
- identity mode is explicit (`transitional` vs `claims`),
- open backend dependencies are listed in section 10.
