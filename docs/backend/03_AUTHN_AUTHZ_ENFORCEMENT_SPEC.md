# Authentication and Authorization Enforcement Specification

Last Updated: 2026-02-12

## 1. Objective

Move from partially enforced auth scaffolding to full runtime enforcement:
- all protected endpoints require valid JWT,
- all board-scoped operations enforce role-based authorization,
- all acting-user identity is claim-derived, never client-supplied.

## 2. Identity Model

### 2.1 Principal source
- JWT bearer token is authoritative.
- Required claims:
  - `sub` or `nameid`: user ID
  - `role`: default/global role

### 2.2 Acting user resolution
- Introduce a single `IUserContext` abstraction.
- `IUserContext.UserId` is required for all authenticated mutations.
- Existing `userId`, `grantedBy`, `updatedBy`, `revokedBy` query parameters are deprecated and ignored in enforcement paths.

## 3. Authorization Policy Model

Define policy names:
- `BoardRead`
- `BoardWrite`
- `BoardManageAccess`
- `BoardDelete`
- `UserSelfOrAdmin`
- `OpsRead`
- `OpsExecute`
- `AutomationReview`
- `ArchiveRestore`

Decision rules:
- `Owner`: all board policies.
- `Admin`: read, write, manage access (not ownership transfer).
- `Editor`: read, write.
- `Viewer`: read only.

## 4. Endpoint Enforcement Matrix

| Endpoint Group | Auth Required | Policy | Notes |
|---|---|---|---|
| `/api/auth/login`, `/api/auth/register` | no | n/a | public entry |
| `/api/auth/change-password` | yes | `UserSelfOrAdmin` | self-change by claim user; admin-only for others |
| `/api/users/*` | yes | `UserSelfOrAdmin` or admin | sensitive PII/account operations |
| `/api/boards` GET/GET by id | yes | `BoardRead` | list filtered by accessible boards |
| `/api/boards` POST/PUT | yes | `BoardWrite` | board create sets owner as claim user |
| `/api/boards/{id}` DELETE | yes | `BoardDelete` | archive or delete based on behavior flag |
| `/api/boards/{id}/columns*` | yes | read/write per verb | reorder requires write |
| `/api/boards/{id}/cards*` | yes | read/write per verb | move requires write |
| `/api/boards/{id}/labels*` | yes | read/write per verb | delete requires write |
| `/api/boards/{id}/access*` | yes | `BoardManageAccess` | acting user from claims only |
| `/api/audit/*` | yes | `BoardRead` and ownership checks | user-level timeline requires admin/self |
| `/api/export/*` | yes | `BoardRead` | export scoped to readable boards |
| `/api/import/*` | yes | `BoardWrite` | import target board permission required |
| `/api/llm-queue*` | yes | queue ownership plus role rules | cancel rules claim-based |
| `/api/archive/*` | yes | `ArchiveRestore` | restore actions audited |
| `/api/automation/proposals*` | yes | `AutomationReview` | create/review/apply rules by risk |
| `/api/ops/*` and `/api/logs*` | yes | `OpsRead` or `OpsExecute` | strict role gates |
| `/api/llm/chat*` | yes | board read/write context checks | command scope validation |

## 5. Migration Plan

### Step 1: Instrumentation and warning mode
- Add deprecation warnings when actor query/body IDs are sent.
- Continue request success while enforcing claims internally.

### Step 2: Hard enforcement
- Remove old parameters from API docs and DTOs.
- Return `400 ValidationError` when unsupported actor parameters are supplied.

### Step 3: Cleanup
- Remove transitional adapter code paths.
- Keep automated compatibility tests for one release cycle.

## 6. Error and Response Standards

Use unified error envelope:
- `errorCode`
- `message`
- `correlationId`
- `details` (optional)

Status mapping:
- `AuthenticationFailed` -> `401`
- `Forbidden` -> `403`
- `ValidationError` -> `400`
- `NotFound` -> `404`
- `Conflict` -> `409`

## 7. Test Requirements

Unit:
- policy evaluator per role and operation.

API integration:
- unauthorized requests rejected for protected endpoints,
- forbidden requests for insufficient role,
- actor parameter deprecation and removal behavior,
- board-scoped access checks for nested routes.

E2E:
- viewer/edit/admin capability boundaries validated through UI actions.

## 8. Acceptance Criteria

- No protected mutation endpoint succeeds without JWT.
- No endpoint relies on caller-provided actor identity.
- Policy matrix behavior is covered by backend integration tests.
