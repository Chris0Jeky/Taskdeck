# Auth and Permissions Rollout Specification

Last Updated: 2026-02-12
Status: Design complete, implementation pending
Priority: First major slice after shell foundation

## 1. Purpose

Define how frontend integrates authentication and board-level permissions while backend transitions from mixed actor-id patterns to claim-based identity.

## 2. Current Reality

Known current state:
- JWT auth endpoints exist (`/api/auth/*`).
- Core board/column/card/label endpoints are not fully claim-enforced yet.
- Several side-track endpoints still require query/body actor ids (`userId`, `grantedBy`, `updatedBy`, `revokedBy`).
- Frontend currently has no auth/session UI and no role-aware gating.

## 3. Target State

- authenticated shell for workspace routes
- route guards and role-aware action availability
- claim-based actor identity for all protected calls
- transitional actor-id adapters removed

## 4. Session Model

Frontend session contract:
- `sessionStore` fields:
  - `token`
  - `userId`
  - `username`
  - `email`
  - `isAuthenticated`
  - `expiresAt`

Storage rule:
- default: in-memory + persisted token storage for restore
- on token invalidation: immediate logout and redirect to `/login`

API behavior:
- attach `Authorization: Bearer <token>` when present
- if `401`, clear session and route to login with return path

## 5. Route Guard Model

Public routes:
- `/login`
- `/register`

Protected routes:
- `/workspace/**`

Guard behavior:
- unauthenticated user: redirect to `/login?redirect=<current>`
- authenticated user on `/login` or `/register`: redirect to `/workspace/boards`

## 6. Permission Model in UI

Board roles: `Owner`, `Admin`, `Editor`, `Viewer`

Action gating summary:
- Viewer: read only
- Editor: card/column/label mutations
- Admin: editor + board access management
- Owner: admin + ownership/destructive controls

Frontend gating policy:
- optimistic pre-check using known role to disable hidden/forbidden actions
- server remains source of truth
- if server denies, show explicit reason using `Forbidden` message

## 7. Transition Plan (Actor IDs -> Claims)

Phase A (transitional)
- UI sends actor-id query/body for endpoints that still require it.
- actor source is `sessionStore.userId`; never free-form user input.

Phase B (dual-ready)
- backend accepts claim-based actor for updated endpoints.
- frontend stops sending actor-id for migrated endpoints.

Phase C (strict claims)
- remove all actor-id request plumbing and related UI assumptions.
- remove transitional tests.

## 8. Feature-Level Rollout Sequence

1. Implement auth views and session store.
2. Add global auth interceptor and route guards.
3. Add board members/access panel using current board access endpoints.
4. Add permission-aware action gating across board workspace.
5. Execute transition phases A->B->C as backend enforcement progresses.

## 9. UX Requirements

Login UX:
- username/email + password
- inline validation
- clear auth failure message (no vague error)

Session UX:
- visible current user in shell
- explicit logout action
- session-expired notification

Permissions UX:
- role badge in board header
- disabled actions show tooltip explaining required role
- board access edits require confirmation for downgrade/removal

## 10. Error and Recovery Behavior

Handle these codes explicitly:
- `AuthenticationFailed`: prompt re-login
- `Forbidden`: show permission explanation
- `ValidationError`: inline form messages
- `Conflict`: explicit state conflict UI + refresh option

Recovery tools:
- retry action button
- refresh board permissions action
- fallback to safe read-only mode when permissions cannot be resolved

## 11. Testing Requirements

Unit:
- route guard redirect logic
- action gating selectors by role
- actor-id adapter utility behavior

Integration:
- login/register/change-password request handling
- board access grant/update/revoke forms
- forbidden and 401 handling flows

E2E:
- register -> login -> board access management scenario
- viewer blocked from write actions
- editor allowed write, blocked from access management
- owner/admin role transitions reflected in UI without reload

## 12. Definition of Done

Auth/permissions slice is complete when:
- workspace routes are protected,
- session lifecycle is stable,
- board role gating is visible and correct,
- board access management is fully operable from UI,
- transitional actor-id handling is isolated and documented,
- tests cover happy path and denial path.
